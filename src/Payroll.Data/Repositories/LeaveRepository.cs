using System.Data;
using Payroll.Core.Abstractions;
using Payroll.Core.Models;
using Payroll.Data.Infrastructure;
namespace Payroll.Data.Repositories;

public sealed class LeaveRepository : ILeaveRepository
{
    private readonly ISqlDatabase _db;
    public LeaveRepository(ISqlDatabase db) => _db = db;
    private static Param I(string n, int? v) => Param.In(n, SqlDbType.Int, v);

    public async Task<IReadOnlyList<LeaveTypeDto>> GetTypesAsync(CancellationToken ct) =>
        await _db.QueryAsync("dbo.usp_LeaveType_List", r => new LeaveTypeDto(r.Int("LeaveTypeId"), r.Str("Code"), r.Str("Name"), r.Dec("AnnualEntitlement"),
            r.Str("AccrualMethod"), r.Dec("MaxCarryForward"), r.Bool("IsPaid"), r.Bool("AllowHalfDay"), r.NInt("MaxConsecutiveDays")), null, ct);

    public async Task<int> SubmitAsync(int employeeId, LeaveRequestInput i, decimal days, int actingUserId, CancellationToken ct)
    {
        var id = Param.Out("NewRequestId", SqlDbType.Int);
        await _db.ExecuteAsync("dbo.usp_Leave_Submit", new[] { I("EmployeeId", employeeId), I("LeaveTypeId", i.LeaveTypeId), Param.Date("FromDate", i.FromDate), Param.Date("ToDate", i.ToDate),
            Param.In("IsHalfDay", SqlDbType.Bit, i.IsHalfDay), Param.Dec("Days", days, 2), Param.Str("Reason", i.Reason, 500), I("ActingUserId", actingUserId), id }, ct);
        return (int)id.Value!;
    }

    public Task DecideAsync(int requestId, bool approve, int actingUserId, int? approverEmployeeId, int? restrictToManagerId, string? remarks, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Leave_Decide", new[] { I("LeaveRequestId", requestId), Param.In("Approve", SqlDbType.Bit, approve), I("ActingUserId", actingUserId),
            I("ApproverEmployeeId", approverEmployeeId), I("RestrictToManagerId", restrictToManagerId), Param.Str("Remarks", remarks, 500) }, ct);

    public Task CancelAsync(int requestId, int actingUserId, int? requestingEmployeeId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Leave_Cancel", new[] { I("LeaveRequestId", requestId), I("ActingUserId", actingUserId), I("RequestingEmployeeId", requestingEmployeeId) }, ct);

    public Task<PagedResult<LeaveRequestDto>> GetRequestsAsync(int? employeeId, string? status, int? managerId, int page, int pageSize, CancellationToken ct) =>
        _db.QueryPagedAsync("dbo.usp_Leave_GetRequests", r => new LeaveRequestDto(r.Int("LeaveRequestId"), r.Int("EmployeeId"), r.Str("EmployeeName"), r.Str("LeaveType"),
            r.Date("FromDate"), r.Date("ToDate"), r.Bool("IsHalfDay"), r.Dec("Days"), r.NStr("Reason"), r.Str("Status"), r.NStr("DecisionRemarks"), r.Utc("CreatedAt")),
            new[] { I("EmployeeId", employeeId), Param.Str("Status", status, 10), I("ManagerId", managerId), I("PageNumber", page), I("PageSize", pageSize) }, page, pageSize, ct);

    public async Task<IReadOnlyList<LeaveBalanceDto>> GetBalancesAsync(int employeeId, int year, CancellationToken ct) =>
        await _db.QueryAsync("dbo.usp_Leave_GetBalances", r => new LeaveBalanceDto(r.Int("LeaveTypeId"), r.Str("Code"), r.Str("Name"), r.Bool("IsPaid"), r.Int("Year"),
            r.Dec("Opening"), r.Dec("Accrued"), r.Dec("Adjusted"), r.Dec("Used"), r.Dec("Balance"), r.Dec("PendingDays")),
            new[] { I("EmployeeId", employeeId), Param.In("Year", SqlDbType.SmallInt, (short)year) }, ct);

    public async Task<IReadOnlyList<LeaveLedgerDto>> GetLedgerAsync(int employeeId, int year, CancellationToken ct) =>
        await _db.QueryAsync("dbo.usp_Leave_GetLedger", r => new LeaveLedgerDto(r.Long("LedgerId"), r.Str("LeaveType"), r.Int("Year"), r.NInt("Month"), r.Str("TxnType"),
            r.Dec("Days"), r.NInt("LeaveRequestId"), r.NStr("Remarks"), r.Utc("CreatedAt")), new[] { I("EmployeeId", employeeId), Param.In("Year", SqlDbType.SmallInt, (short)year) }, ct);

    public async Task<int> RunMonthlyAccrualAsync(int year, int month, int actingUserId, CancellationToken ct) =>
        (await _db.QueryAsync("dbo.usp_Leave_RunMonthlyAccrual", r => r.Int("PostedCount"),
            new[] { Param.In("Year", SqlDbType.SmallInt, (short)year), Param.In("Month", SqlDbType.TinyInt, (byte)month), I("ActingUserId", actingUserId) }, ct)).First();

    public async Task<int> CarryForwardAsync(int fromYear, int actingUserId, CancellationToken ct) =>
        (await _db.QueryAsync("dbo.usp_Leave_YearEndCarryForward", r => r.Int("PostedCount"),
            new[] { Param.In("FromYear", SqlDbType.SmallInt, (short)fromYear), I("ActingUserId", actingUserId) }, ct)).First();

    public Task AdjustAsync(LeaveAdjustInput i, int actingUserId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Leave_Adjust", new[] { I("EmployeeId", i.EmployeeId), I("LeaveTypeId", i.LeaveTypeId), Param.In("Year", SqlDbType.SmallInt, (short)i.Year),
            Param.Dec("Days", i.Days, 2), Param.Str("Remarks", i.Remarks, 300), I("ActingUserId", actingUserId) }, ct);
}
