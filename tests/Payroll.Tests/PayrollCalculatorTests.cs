using Payroll.Core.Calculation;
using Payroll.Core.Domain;
using Payroll.Core.Models;
using Xunit;
namespace Payroll.Tests;

public class PayrollCalculatorTests
{
    private static decimal Line(PayslipResult r, string type, string code) => r.Lines.Single(l => l.LineType == type && l.Code == code).Amount;

    [Fact]
    public void Full_month_with_allowances_statutory_and_tax()
    {
        var set = TestData.March2026();
        var emp = TestData.Employee();
        emp.Components.Add(new CalcComponent("HRA", "House rent", ComponentKind.Earning, CalcMethod.PercentOfBasic, true, 40));
        emp.Components.Add(new CalcComponent("TRANSPORT", "Transport", ComponentKind.Earning, CalcMethod.Fixed, false, 2000));

        var r = TestData.Calculator().Calculate(set, emp)!;

        Assert.Equal(22, r.WorkingDays);
        Assert.Equal(22, r.PaidDays);
        Assert.Equal(72000m, r.GrossEarnings);                       // 50,000 + 20,000 + 2,000
        Assert.Equal(1800m, Line(r, LineType.Deduction, "PF"));      // 12% of the 15,000 ceiling
        Assert.Equal(1800m, Line(r, LineType.Employer, "PF"));
        Assert.Equal(68200m, r.TaxableIncome);                       // 72,000 - 2,000 non-taxable - 1,800 pre-tax PF
        Assert.Equal(1488.07m, Line(r, LineType.Deduction, "TDS"));  // (743,400-400,000)*5% = 17,170 + 4% cess = 17,856.80 / 12
        Assert.Equal(3288.07m, r.TotalDeductions);
        Assert.Equal(68711.93m, r.NetPay);
        Assert.Equal(1800m, r.EmployerContributions);
    }

    [Fact]
    public void Unpaid_leave_prorates_basic_by_working_days()
    {
        var set = TestData.March2026();
        var emp = TestData.Employee();
        emp.UnpaidLeaves.Add(new CalcLeave(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 11), false, 2));

        var r = TestData.Calculator().Calculate(set, emp)!;

        Assert.Equal(2, r.LopDays);
        Assert.Equal(20, r.PaidDays);
        Assert.Equal(45454.55m, Line(r, LineType.Earning, "BASIC"));  // 50,000 x 20/22
    }

    [Fact]
    public void Leave_spanning_weekend_only_counts_working_days()
    {
        var set = TestData.March2026();
        var emp = TestData.Employee();
        emp.UnpaidLeaves.Add(new CalcLeave(new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 9), false, 2));   // Fri..Mon = 2 working days

        Assert.Equal(2, TestData.Calculator().Calculate(set, emp)!.LopDays);
    }

    [Fact]
    public void Mid_month_joiner_is_prorated_from_join_date()
    {
        var set = TestData.March2026();
        var emp = TestData.Employee(join: new DateOnly(2026, 3, 16));      // 12 working days remain (16-20, 23-27, 30-31)

        var r = TestData.Calculator().Calculate(set, emp)!;

        Assert.Equal(12, r.PaidDays);
        Assert.Equal(27272.73m, Line(r, LineType.Earning, "BASIC"));      // 50,000 x 12/22
    }

    [Fact]
    public void Employee_not_yet_joined_gets_no_payslip() =>
        Assert.Null(TestData.Calculator().Calculate(TestData.March2026(), TestData.Employee(join: new DateOnly(2026, 4, 1))));

    [Fact]
    public void Overtime_uses_hourly_rate_and_multiplier()
    {
        var emp = TestData.Employee(overtime: true);
        emp.OvertimeHours = 10; emp.OvertimeWeightedHours = 15;            // 10 hours at 1.5x

        var r = TestData.Calculator().Calculate(TestData.March2026(), emp)!;

        Assert.Equal(4687.50m, r.OvertimePay);                              // 50,000/160 = 312.50 x 15
        Assert.Equal(54687.50m, r.GrossEarnings);
    }

    [Fact]
    public void Overtime_is_ignored_for_ineligible_employee_with_warning()
    {
        var emp = TestData.Employee(overtime: false);
        emp.OvertimeHours = 5; emp.OvertimeWeightedHours = 7.5m;

        var r = TestData.Calculator().Calculate(TestData.March2026(), emp)!;

        Assert.Equal(0, r.OvertimePay);
        Assert.Single(r.Warnings);
    }

    [Fact]
    public void Overtime_is_capped_per_month()
    {
        var emp = TestData.Employee(overtime: true);
        emp.OvertimeHours = 120; emp.OvertimeWeightedHours = 180;           // cap is 60 hours => half

        var r = TestData.Calculator().Calculate(TestData.March2026(), emp)!;

        Assert.Equal(60, r.OvertimeHours);
        Assert.Equal(28125.00m, r.OvertimePay);                              // 312.50 x 90
    }

    [Fact]
    public void Loan_recovery_is_limited_to_percentage_of_net_and_never_exceeds_outstanding()
    {
        var emp = TestData.Employee();
        emp.Loans.Add(new CalcLoan(1, "Advance", 100000, 100000));          // far more than the 50% cap allows

        var r = TestData.Calculator().Calculate(TestData.March2026(), emp)!;

        var netBeforeLoan = r.GrossEarnings - (r.TotalDeductions - Line(r, LineType.Deduction, "LOAN"));
        Assert.Equal(Money.Round(netBeforeLoan / 2), Line(r, LineType.Deduction, "LOAN"));
        Assert.True(r.NetPay > 0);
        Assert.Contains(r.Warnings, w => w.Contains("Loan #1"));
        Assert.Equal(1, r.Lines.Single(l => l.Code == "LOAN").ReferenceId);
    }

    [Fact]
    public void Final_instalment_is_limited_to_outstanding_balance()
    {
        var emp = TestData.Employee();
        emp.Loans.Add(new CalcLoan(2, "Advance", 5000, 1200));

        Assert.Equal(1200m, Line(TestData.Calculator().Calculate(TestData.March2026(), emp)!, LineType.Deduction, "LOAN"));
    }

    [Fact]
    public void Missing_salary_structure_is_a_business_error()
    {
        var emp = new EmployeePayrollInput { EmployeeId = 1, Code = "E1", JoinDate = new DateOnly(2020, 1, 1) };
        Assert.Throws<BusinessRuleException>(() => TestData.Calculator().Calculate(TestData.March2026(), emp));
    }

    [Fact]
    public void Net_pay_equals_gross_minus_deductions_and_lines_reconcile()
    {
        var emp = TestData.Employee();
        emp.Components.Add(new CalcComponent("HRA", "HRA", ComponentKind.Earning, CalcMethod.PercentOfBasic, true, 40));
        emp.Components.Add(new CalcComponent("CANTEEN", "Canteen", ComponentKind.Deduction, CalcMethod.Fixed, false, 350));
        var r = TestData.Calculator().Calculate(TestData.March2026(), emp)!;

        Assert.Equal(r.GrossEarnings, r.Lines.Where(l => l.LineType == LineType.Earning).Sum(l => l.Amount));
        Assert.Equal(r.TotalDeductions, r.Lines.Where(l => l.LineType == LineType.Deduction).Sum(l => l.Amount));
        Assert.Equal(r.GrossEarnings - r.TotalDeductions, r.NetPay);
    }
}
