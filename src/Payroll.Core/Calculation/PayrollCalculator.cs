using Microsoft.Extensions.Options;
using Payroll.Core.Configuration;
using Payroll.Core.Domain;
using Payroll.Core.Models;
namespace Payroll.Core.Calculation;

public interface IPayrollCalculator
{
    /// <summary>Returns null when the employee was not employed at all during the period.</summary>
    PayslipResult? Calculate(PayrollInputSet set, EmployeePayrollInput employee);
}

/// <summary>
/// Pure, deterministic payroll algorithm (no I/O) so it can be unit-tested exhaustively.
/// Order: attendance/LOP -> earnings -> overtime -> statutory -> tax -> other deductions -> loans -> net.
/// </summary>
public sealed class PayrollCalculator : IPayrollCalculator
{
    private readonly WorkingCalendar _calendar;
    private readonly ITaxCalculator _tax;
    private readonly PayrollOptions _opt;

    public PayrollCalculator(WorkingCalendar calendar, ITaxCalculator tax, IOptions<PayrollOptions> options)
        => (_calendar, _tax, _opt) = (calendar, tax, options.Value);

    public PayslipResult? Calculate(PayrollInputSet set, EmployeePayrollInput emp)
    {
        if (emp.BasicSalary is not { } basic || basic <= 0)
            throw new BusinessRuleException($"{emp.Code}: no effective salary structure for the period.");

        var p = set.Period;
        var start = emp.JoinDate > p.FirstDay ? emp.JoinDate : p.FirstDay;
        var end = emp.ExitDate is { } exit && exit < p.LastDay ? exit : p.LastDay;
        if (start > end) return null;

        // ---- 1. attendance: working days, loss-of-pay, pro-ration ratio ----
        var monthWorking = _calendar.CountWorkingDays(p.FirstDay, p.LastDay, set.Holidays);
        if (monthWorking == 0) throw new BusinessRuleException($"{emp.Code}: the period has no working days.");
        var employedWorking = _calendar.CountWorkingDays(start, end, set.Holidays);
        var lop = 0m;
        foreach (var leave in emp.UnpaidLeaves)
        {
            var s = leave.From > start ? leave.From : start;
            var e = leave.To < end ? leave.To : end;
            if (s > e) continue;
            lop += leave.IsHalfDay ? (_calendar.IsWorkingDay(s, set.Holidays) ? 0.5m : 0m) : _calendar.CountWorkingDays(s, e, set.Holidays);
        }
        lop = Math.Min(lop, employedWorking);
        var paidDays = employedWorking - lop;
        var ratio = paidDays / monthWorking;

        var r = new PayslipResult { EmployeeId = emp.EmployeeId, EmployeeCode = emp.Code, DepartmentId = emp.DepartmentId };
        decimal gross = 0, taxable = 0, totalDeductions = 0, employer = 0;
        void Earn(string code, string name, decimal amount, bool isTaxable, short order)
        {
            if (amount == 0) return;
            r.Lines.Add(new PayslipLineResult(LineType.Earning, code, name, amount, false, null, order));
            gross += amount; if (isTaxable) taxable += amount;
        }
        void Deduct(string code, string name, decimal amount, bool statutory, int? reference, short order)
        {
            if (amount <= 0) return;
            r.Lines.Add(new PayslipLineResult(LineType.Deduction, code, name, amount, statutory, reference, order));
            totalDeductions += amount;
        }

        // ---- 2. earnings (pro-rated for unpaid days / part-month employment) ----
        var earnedBasic = Money.Round(basic * ratio);
        Earn(LineCodes.Basic, "Basic salary", earnedBasic, true, 10);
        short order = 20;
        foreach (var c in emp.Components.Where(c => c.ComponentType == ComponentKind.Earning))
        {
            var amount = c.CalcMethod == CalcMethod.PercentOfBasic ? earnedBasic * c.Value / 100m : c.Value * ratio;
            Earn(c.Code, c.Name, Money.Round(amount), c.IsTaxable, order++);
        }

        // ---- 3. overtime: (basic / standard hours) x sum(hours x multiplier), capped per month ----
        decimal otPay = 0, otHours = emp.OvertimeHours;
        if (emp.OvertimeHours > 0 && !emp.OvertimeEligible)
        {
            r.Warnings.Add($"{emp.OvertimeHours:0.##} approved overtime hours ignored: employee is not overtime-eligible.");
            otHours = 0;
        }
        else if (emp.OvertimeHours > 0)
        {
            var weighted = emp.OvertimeWeightedHours;
            if (emp.OvertimeHours > _opt.MaxOvertimeHoursPerMonth)
            {
                weighted = weighted * _opt.MaxOvertimeHoursPerMonth / emp.OvertimeHours;
                otHours = _opt.MaxOvertimeHoursPerMonth;
                r.Warnings.Add($"Overtime capped at {_opt.MaxOvertimeHoursPerMonth:0.##} hours (had {emp.OvertimeHours:0.##}).");
            }
            otPay = Money.Round(basic / emp.StandardHoursPerMonth * weighted);
            Earn(LineCodes.Overtime, "Overtime", otPay, true, 90);
        }

        // ---- 4. statutory contributions ----
        decimal preTax = 0;
        short stat = 100;
        foreach (var rule in set.Statutory)
        {
            var wageBase = rule.BaseOn == "Gross" ? gross : earnedBasic;
            if (rule.WageCeiling is { } ceiling && wageBase > ceiling) wageBase = ceiling;
            var employeeShare = Money.Round(wageBase * rule.EmployeeRatePercent / 100m);
            var employerShare = Money.Round(wageBase * rule.EmployerRatePercent / 100m);
            Deduct(rule.Code, rule.Name, employeeShare, true, null, stat);
            if (rule.IsPreTax) preTax += employeeShare;
            if (employerShare > 0)
            {
                r.Lines.Add(new PayslipLineResult(LineType.Employer, rule.Code, rule.Name + " (employer)", employerShare, true, null, (short)(900 + stat - 100)));
                employer += employerShare;
            }
            stat++;
        }

        // ---- 5. income tax withholding ----
        var monthlyTaxable = Math.Max(0, taxable - preTax);
        decimal tds = 0;
        if (set.TaxParameter is null || set.Slabs.Count == 0)
        {
            if (monthlyTaxable > 0) r.Warnings.Add($"No tax slabs configured for financial year {p.FinancialYear}; tax not withheld.");
        }
        else tds = _tax.CalculateMonthlyTax(monthlyTaxable, set.Slabs, set.TaxParameter);
        Deduct(LineCodes.Tax, "Income tax (TDS)", tds, true, null, 150);

        // ---- 6. other deductions ----
        order = 200;
        foreach (var c in emp.Components.Where(c => c.ComponentType == ComponentKind.Deduction))
        {
            var amount = c.CalcMethod == CalcMethod.PercentOfBasic ? earnedBasic * c.Value / 100m : c.Value;
            Deduct(c.Code, c.Name, Money.Round(amount), false, null, order++);
        }

        // ---- 7. loan recovery (last, capped at X% of net-before-loan so pay never goes negative) ----
        var netBeforeLoan = gross - totalDeductions;
        var available = Math.Max(0, netBeforeLoan) * _opt.LoanRecoveryCapPercentOfNet / 100m;
        order = 300;
        foreach (var loan in emp.Loans.OrderBy(l => l.LoanId))
        {
            var due = Math.Min(loan.Installment, loan.Outstanding);
            var amount = Money.Round(Math.Min(due, available));
            if (amount < due) r.Warnings.Add($"Loan #{loan.LoanId} instalment reduced from {due:0.00} to {amount:0.00} (recovery cap).");
            Deduct(LineCodes.Loan, $"{loan.LoanType} recovery (#{loan.LoanId})", amount, false, loan.LoanId, order++);
            available -= amount;
        }

        r.BasicSalary = basic;
        r.CalendarDays = p.CalendarDays;
        r.WorkingDays = monthWorking;
        r.PaidDays = paidDays;
        r.LopDays = lop;
        r.OvertimeHours = otHours;
        r.OvertimePay = otPay;
        r.GrossEarnings = Money.Round(gross);
        r.TaxableIncome = Money.Round(monthlyTaxable);
        r.TotalDeductions = Money.Round(totalDeductions);
        r.NetPay = r.GrossEarnings - r.TotalDeductions;
        r.EmployerContributions = Money.Round(employer);
        if (r.NetPay < 0) r.Warnings.Add($"Deductions exceed gross earnings (net pay {r.NetPay:0.00}).");
        r.Lines.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        return r;
    }
}
