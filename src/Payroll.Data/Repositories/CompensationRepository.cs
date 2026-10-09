using System.Data;
using Payroll.Core.Abstractions;
using Payroll.Core.Models;
using Payroll.Data.Infrastructure;
namespace Payroll.Data.Repositories;

public sealed class CompensationRepository : ICompensationRepository
{
    private readonly ISqlDatabase _db;
    public CompensationRepository(ISqlDatabase db) => _db = db;
    private static Param I(string n, int? v) => Param.In(n, SqlDbType.Int, v);

    public async Task<IReadOnlyList<SalaryStructureDto>> GetSalaryHistoryAsync(int employeeId, CancellationToken ct) =>
        await _db.QueryAsync("dbo.usp_Salary_GetHistory", r => new SalaryStructureDto(r.Int("Id"), r.Int("EmployeeId"), r.Date("EffectiveFrom"), r.NDate("EffectiveTo"),
            r.Dec("BasicSalary"), r.Dec("StandardHoursPerMonth"), r.Bool("OvertimeEligible")), new[] { I("EmployeeId", employeeId) }, ct);

    public Task SetSalaryAsync(SalaryInput i, int actingUserId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Salary_SetStructure", new[]
        {
            I("EmployeeId", i.EmployeeId), Param.Date("EffectiveFrom", i.EffectiveFrom), Param.Dec("BasicSalary", i.BasicSalary, 2),
            Param.Dec("StandardHoursPerMonth", i.StandardHoursPerMonth, 2), Param.In("OvertimeEligible", SqlDbType.Bit, i.OvertimeEligible), I("ActingUserId", actingUserId)
        }, ct);

    public async Task<IReadOnlyList<SalaryComponentDef>> GetComponentDefsAsync(CancellationToken ct) =>
        await _db.QueryAsync("dbo.usp_Component_List", r => new SalaryComponentDef(r.Int("ComponentId"), r.Str("Code"), r.Str("Name"), r.Str("ComponentType"), r.Str("CalcMethod"), r.Bool("IsTaxable")), null, ct);

    public async Task<IReadOnlyList<EmployeeComponentDto>> GetEmployeeComponentsAsync(int employeeId, CancellationToken ct) =>
        await _db.QueryAsync("dbo.usp_EmployeeComponent_GetByEmployee", r => new EmployeeComponentDto(r.Int("EmployeeComponentId"), r.Int("ComponentId"), r.Str("Code"), r.Str("Name"),
            r.Str("ComponentType"), r.Str("CalcMethod"), r.Dec("Value"), r.Date("EffectiveFrom"), r.NDate("EffectiveTo")), new[] { I("EmployeeId", employeeId) }, ct);

    public Task AssignComponentAsync(ComponentAssignInput i, int actingUserId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_EmployeeComponent_Assign", new[] { I("EmployeeId", i.EmployeeId), I("ComponentId", i.ComponentId), Param.Dec("Value", i.Value, 4),
            Param.Date("EffectiveFrom", i.EffectiveFrom), I("ActingUserId", actingUserId) }, ct);

    public Task EndComponentAsync(int id, DateOnly endDate, int actingUserId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_EmployeeComponent_End", new[] { I("EmployeeComponentId", id), Param.Date("EndDate", endDate), I("ActingUserId", actingUserId) }, ct);

    public async Task<int> SubmitOvertimeAsync(int employeeId, OvertimeInput i, int actingUserId, CancellationToken ct)
    {
        var id = Param.Out("NewOvertimeId", SqlDbType.Int);
        await _db.ExecuteAsync("dbo.usp_Overtime_Submit", new[] { I("EmployeeId", employeeId), Param.Date("WorkDate", i.WorkDate), Param.Dec("Hours", i.Hours, 2),
            Param.Dec("Multiplier", i.Multiplier, 2), Param.Str("Remarks", i.Remarks, 300), I("ActingUserId", actingUserId), id }, ct);
        return (int)id.Value!;
    }

    public Task DecideOvertimeAsync(int overtimeId, bool approve, int actingUserId, int? approverEmployeeId, int? restrictToManagerId, string? remarks, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Overtime_Decide", new[] { I("OvertimeId", overtimeId), Param.In("Approve", SqlDbType.Bit, approve), I("ActingUserId", actingUserId),
            I("ApproverEmployeeId", approverEmployeeId), I("RestrictToManagerId", restrictToManagerId), Param.Str("Remarks", remarks, 300) }, ct);

    public Task<PagedResult<OvertimeDto>> ListOvertimeAsync(string? status, int? employeeId, int? managerId, int page, int pageSize, CancellationToken ct) =>
        _db.QueryPagedAsync("dbo.usp_Overtime_List", r => new OvertimeDto(r.Int("OvertimeId"), r.Int("EmployeeId"), r.Str("EmployeeName"), r.Date("WorkDate"),
            r.Dec("Hours"), r.Dec("Multiplier"), r.Str("Status"), r.NStr("Remarks")),
            new[] { Param.Str("Status", status, 10), I("EmployeeId", employeeId), I("ManagerId", managerId), I("PageNumber", page), I("PageSize", pageSize) }, page, pageSize, ct);

    public async Task<int> CreateLoanAsync(LoanInput i, int actingUserId, CancellationToken ct)
    {
        var id = Param.Out("NewLoanId", SqlDbType.Int);
        await _db.ExecuteAsync("dbo.usp_Loan_Create", new[] { I("EmployeeId", i.EmployeeId), Param.Str("LoanType", i.LoanType, 40), Param.Dec("Principal", i.Principal, 2),
            Param.Dec("Installment", i.Installment, 2), Param.Date("StartDate", i.StartDate), I("ActingUserId", actingUserId), id }, ct);
        return (int)id.Value!;
    }

    public async Task<IReadOnlyList<LoanDto>> ListLoansAsync(int? employeeId, string? status, CancellationToken ct) =>
        await _db.QueryAsync("dbo.usp_Loan_List", r => new LoanDto(r.Int("LoanId"), r.Int("EmployeeId"), r.Str("EmployeeName"), r.Str("LoanType"), r.Dec("PrincipalAmount"),
            r.Dec("InstallmentAmount"), r.Dec("OutstandingBalance"), r.Date("StartDate"), r.Str("Status")), new[] { I("EmployeeId", employeeId), Param.Str("Status", status, 10) }, ct);

    public Task CancelLoanAsync(int loanId, int actingUserId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Loan_Cancel", new[] { I("LoanId", loanId), I("ActingUserId", actingUserId) }, ct);

    public async Task<IReadOnlyList<HolidayDto>> GetHolidaysAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await _db.QueryAsync("dbo.usp_Holiday_GetRange", r => new HolidayDto(r.Date("HolidayDate"), r.Str("Name")), new[] { Param.Date("From", from), Param.Date("To", to) }, ct);

    public Task UpsertHolidayAsync(DateOnly date, string name, int actingUserId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Holiday_Upsert", new[] { Param.Date("HolidayDate", date), Param.Str("Name", name, 100), I("ActingUserId", actingUserId) }, ct);

    public Task DeleteHolidayAsync(DateOnly date, int actingUserId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Holiday_Delete", new[] { Param.Date("HolidayDate", date), I("ActingUserId", actingUserId) }, ct);
}
