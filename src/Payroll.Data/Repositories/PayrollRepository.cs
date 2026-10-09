using System.Data;
using Payroll.Core.Abstractions;
using Payroll.Core.Models;
using Payroll.Data.Infrastructure;
namespace Payroll.Data.Repositories;

public sealed class PayrollRepository : IPayrollRepository
{
    private readonly ISqlDatabase _db;
    public PayrollRepository(ISqlDatabase db) => _db = db;
    private static Param I(string n, int? v) => Param.In(n, SqlDbType.Int, v);

    private static PayrollRunDto MapRun(Microsoft.Data.SqlClient.SqlDataReader r) => new(r.Int("PayrollRunId"), r.Int("PeriodYear"), r.Int("PeriodMonth"), r.Str("Status"),
        r.Int("EmployeeCount"), r.Dec("TotalGross"), r.Dec("TotalDeductions"), r.Dec("TotalNet"), r.Dec("TotalEmployerCost"), r.NUtc("ProcessedAt"), r.NUtc("ApprovedAt"), r.NUtc("PaidAt"));

    public async Task<IReadOnlyList<PayrollRunDto>> GetRunsAsync(CancellationToken ct) => await _db.QueryAsync("dbo.usp_Payroll_GetRuns", MapRun, null, ct);
    public Task<PayrollRunDto?> GetRunAsync(int runId, CancellationToken ct) => _db.QuerySingleAsync("dbo.usp_Payroll_GetRun", MapRun, new[] { I("RunId", runId) }, ct);

    public async Task<int> CreateRunAsync(int year, int month, int actingUserId, CancellationToken ct)
    {
        var id = Param.Out("NewRunId", SqlDbType.Int);
        await _db.ExecuteAsync("dbo.usp_Payroll_CreateRun", new[] { Param.In("Year", SqlDbType.SmallInt, (short)year), Param.In("Month", SqlDbType.TinyInt, (byte)month), I("ActingUserId", actingUserId), id }, ct);
        return (int)id.Value!;
    }

    /// <summary>Reads the 10 result sets of usp_Payroll_GetCalculationInputs in order (see the stored procedure for the contract).</summary>
    public async Task<PayrollInputSet> GetCalculationInputsAsync(int runId, int fyStartMonth, CancellationToken ct)
    {
        PayrollInputSet? set = null;
        await _db.QueryMultipleAsync("dbo.usp_Payroll_GetCalculationInputs",
            new[] { I("RunId", runId), Param.In("FyStartMonth", SqlDbType.TinyInt, (byte)fyStartMonth) }, async (r, token) =>
        {
            await r.ReadAsync(token);                                                                                  // 1 run
            set = new PayrollInputSet
            {
                RunId = runId, RunStatus = r.Str("Status"),
                Period = new PayrollPeriod { Year = r.Int("PeriodYear"), Month = r.Int("PeriodMonth"), FinancialYear = r.Int("FinancialYear") }
            };
            var byId = new Dictionary<int, EmployeePayrollInput>();
            await r.NextResultAsync(token);                                                                            // 2 employees
            while (await r.ReadAsync(token))
            {
                var e = new EmployeePayrollInput
                {
                    EmployeeId = r.Int("EmployeeId"), Code = r.Str("EmployeeCode"), Name = r.Str("FullName"), DepartmentId = r.Int("DepartmentId"),
                    JoinDate = r.Date("JoinDate"), ExitDate = r.NDate("ExitDate"), BasicSalary = r.NDec("BasicSalary"),
                    StandardHoursPerMonth = r.NDec("StandardHoursPerMonth") ?? 160m, OvertimeEligible = !r.IsNull("OvertimeEligible") && r.Bool("OvertimeEligible")
                };
                byId[e.EmployeeId] = e; set.Employees.Add(e);
            }
            await r.NextResultAsync(token);                                                                            // 3 components
            while (await r.ReadAsync(token))
                if (byId.TryGetValue(r.Int("EmployeeId"), out var e))
                    e.Components.Add(new CalcComponent(r.Str("Code"), r.Str("Name"), r.Str("ComponentType"), r.Str("CalcMethod"), r.Bool("IsTaxable"), r.Dec("Value")));
            await r.NextResultAsync(token);                                                                            // 4 overtime
            while (await r.ReadAsync(token))
                if (byId.TryGetValue(r.Int("EmployeeId"), out var e)) { e.OvertimeHours = r.Dec("Hours"); e.OvertimeWeightedHours = r.Dec("WeightedHours"); }
            await r.NextResultAsync(token);                                                                            // 5 unpaid leave
            while (await r.ReadAsync(token))
                if (byId.TryGetValue(r.Int("EmployeeId"), out var e)) e.UnpaidLeaves.Add(new CalcLeave(r.Date("FromDate"), r.Date("ToDate"), r.Bool("IsHalfDay"), r.Dec("Days")));
            await r.NextResultAsync(token);                                                                            // 6 holidays
            while (await r.ReadAsync(token)) set.Holidays.Add(r.Date("HolidayDate"));
            await r.NextResultAsync(token);                                                                            // 7 loans
            while (await r.ReadAsync(token))
                if (byId.TryGetValue(r.Int("EmployeeId"), out var e)) e.Loans.Add(new CalcLoan(r.Int("LoanId"), r.Str("LoanType"), r.Dec("InstallmentAmount"), r.Dec("OutstandingBalance")));
            await r.NextResultAsync(token);                                                                            // 8 statutory
            while (await r.ReadAsync(token))
                set.Statutory.Add(new StatutoryRuleDto(r.Str("Code"), r.Str("Name"), r.Dec("EmployeeRatePercent"), r.Dec("EmployerRatePercent"), r.NDec("WageCeiling"), r.Str("BaseOn"), r.Bool("IsPreTax")));
            await r.NextResultAsync(token);                                                                            // 9 slabs
            while (await r.ReadAsync(token)) set.Slabs.Add(new TaxSlabDto(r.Dec("FromAmount"), r.NDec("ToAmount"), r.Dec("RatePercent")));
            await r.NextResultAsync(token);                                                                            // 10 tax parameters
            if (await r.ReadAsync(token)) set.TaxParameter = new TaxParameterDto(r.Dec("AnnualStandardDeduction"), r.Dec("CessPercent"));
        }, ct);
        return set ?? throw new InvalidOperationException("Calculation inputs were not returned.");
    }

    /// <summary>Bulk-saves all payslips with two table-valued parameters. Column order MUST match dbo.PayslipTableType / dbo.PayslipLineTableType.</summary>
    public Task SavePayslipsAsync(int runId, int actingUserId, IReadOnlyList<PayslipResult> payslips, CancellationToken ct)
    {
        var slips = new DataTable();
        foreach (var (name, type) in new (string, Type)[] { ("EmployeeId", typeof(int)), ("DepartmentId", typeof(int)), ("BasicSalary", typeof(decimal)), ("CalendarDays", typeof(byte)),
            ("WorkingDays", typeof(decimal)), ("PaidDays", typeof(decimal)), ("LopDays", typeof(decimal)), ("OvertimeHours", typeof(decimal)), ("OvertimePay", typeof(decimal)),
            ("GrossEarnings", typeof(decimal)), ("TaxableIncome", typeof(decimal)), ("TotalDeductions", typeof(decimal)), ("NetPay", typeof(decimal)), ("EmployerContributions", typeof(decimal)) })
            slips.Columns.Add(name, type);
        var lines = new DataTable();
        foreach (var (name, type) in new (string, Type)[] { ("EmployeeId", typeof(int)), ("LineType", typeof(string)), ("Code", typeof(string)), ("Description", typeof(string)),
            ("Amount", typeof(decimal)), ("IsStatutory", typeof(bool)), ("ReferenceId", typeof(int)), ("SortOrder", typeof(short)) })
            lines.Columns.Add(name, type);

        foreach (var p in payslips)
        {
            slips.Rows.Add(p.EmployeeId, p.DepartmentId, p.BasicSalary, (byte)p.CalendarDays, p.WorkingDays, p.PaidDays, p.LopDays, p.OvertimeHours, p.OvertimePay,
                p.GrossEarnings, p.TaxableIncome, p.TotalDeductions, p.NetPay, p.EmployerContributions);
            foreach (var l in p.Lines)
                lines.Rows.Add(p.EmployeeId, l.LineType, l.Code, l.Description, l.Amount, l.IsStatutory, (object?)l.ReferenceId ?? DBNull.Value, l.SortOrder);
        }
        return _db.ExecuteAsync("dbo.usp_Payroll_SavePayslips", new[]
        {
            I("RunId", runId), I("ActingUserId", actingUserId),
            Param.Tvp("Payslips", "dbo.PayslipTableType", slips), Param.Tvp("Lines", "dbo.PayslipLineTableType", lines)
        }, ct);
    }

    public Task ApproveAsync(int runId, int actingUserId, bool enforceSeparation, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Payroll_Approve", new[] { I("RunId", runId), I("ActingUserId", actingUserId), Param.In("EnforceSeparation", SqlDbType.Bit, enforceSeparation) }, ct);

    public Task MarkPaidAsync(int runId, int actingUserId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Payroll_MarkPaid", new[] { I("RunId", runId), I("ActingUserId", actingUserId) }, ct);

    public async Task<PayslipDetailDto?> GetPayslipAsync(int payslipId, int? requestingEmployeeId, CancellationToken ct)
    {
        PayslipDetailDto? dto = null;
        await _db.QueryMultipleAsync("dbo.usp_Payslip_Get", new[] { I("PayslipId", payslipId), I("RequestingEmployeeId", requestingEmployeeId) }, async (r, token) =>
        {
            if (await r.ReadAsync(token))
                dto = new PayslipDetailDto
                {
                    PayslipId = r.Int("PayslipId"), PayrollRunId = r.Int("PayrollRunId"), PeriodYear = r.Int("PeriodYear"), PeriodMonth = r.Int("PeriodMonth"),
                    RunStatus = r.Str("RunStatus"), EmployeeId = r.Int("EmployeeId"), EmployeeCode = r.Str("EmployeeCode"), FullName = r.Str("FullName"),
                    Department = r.Str("Department"), Designation = r.Str("Designation"), TaxIdNumber = r.NStr("TaxIdNumber"), BankName = r.NStr("BankName"),
                    BankAccountMasked = r.NStr("BankAccountMasked"), JoinDate = r.Date("JoinDate"), BasicSalary = r.Dec("BasicSalary"), CalendarDays = r.Int("CalendarDays"),
                    WorkingDays = r.Dec("WorkingDays"), PaidDays = r.Dec("PaidDays"), LopDays = r.Dec("LopDays"), OvertimeHours = r.Dec("OvertimeHours"), OvertimePay = r.Dec("OvertimePay"),
                    GrossEarnings = r.Dec("GrossEarnings"), TaxableIncome = r.Dec("TaxableIncome"), TotalDeductions = r.Dec("TotalDeductions"), NetPay = r.Dec("NetPay"),
                    EmployerContributions = r.Dec("EmployerContributions")
                };
            await r.NextResultAsync(token);
            while (dto is not null && await r.ReadAsync(token))
                dto.Lines.Add(new PayslipLineDto(r.Str("LineType"), r.Str("Code"), r.Str("Description"), r.Dec("Amount"), r.Bool("IsStatutory")));
        }, ct);
        return dto;
    }

    public async Task<IReadOnlyList<PayslipListItem>> ListPayslipsAsync(int employeeId, bool onlyFinalised, CancellationToken ct) =>
        await _db.QueryAsync("dbo.usp_Payslip_ListByEmployee", r => new PayslipListItem(r.Int("PayslipId"), r.Int("PeriodYear"), r.Int("PeriodMonth"), r.Dec("GrossEarnings"),
            r.Dec("TotalDeductions"), r.Dec("NetPay"), r.Str("RunStatus")), new[] { I("EmployeeId", employeeId), Param.In("OnlyFinalised", SqlDbType.Bit, onlyFinalised) }, ct);
}
