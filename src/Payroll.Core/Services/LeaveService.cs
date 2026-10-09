using Microsoft.Extensions.Logging;
using Payroll.Core.Abstractions;
using Payroll.Core.Calculation;
using Payroll.Core.Domain;
using Payroll.Core.Models;
namespace Payroll.Core.Services;

public interface ILeaveService
{
    Task<IReadOnlyList<LeaveTypeDto>> GetTypesAsync(CancellationToken ct = default);
    Task<int> SubmitAsync(LeaveRequestInput input, CancellationToken ct = default);
    Task DecideAsync(int requestId, bool approve, string? remarks, CancellationToken ct = default);
    Task CancelAsync(int requestId, CancellationToken ct = default);
    Task<PagedResult<LeaveRequestDto>> GetMyRequestsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<LeaveRequestDto>> GetPendingApprovalsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveBalanceDto>> GetBalancesAsync(int? employeeId, int year, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveLedgerDto>> GetLedgerAsync(int employeeId, int year, CancellationToken ct = default);
    Task<int> RunMonthlyAccrualAsync(int year, int month, CancellationToken ct = default);
    Task<(int CarryForward, int JanuaryAccrual)> RunYearEndAsync(int closingYear, CancellationToken ct = default);
    Task AdjustAsync(LeaveAdjustInput input, CancellationToken ct = default);
}

public sealed class LeaveService : ILeaveService
{
    private readonly ILeaveRepository _repo;
    private readonly ICompensationRepository _comp;
    private readonly LeaveCalculator _calc;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly ILogger<LeaveService> _log;

    public LeaveService(ILeaveRepository repo, ICompensationRepository comp, LeaveCalculator calc, ITransactionRunner tx, ICurrentUser user, ILogger<LeaveService> log)
        => (_repo, _comp, _calc, _tx, _user, _log) = (repo, comp, calc, tx, user, log);

    public Task<IReadOnlyList<LeaveTypeDto>> GetTypesAsync(CancellationToken ct = default) { _user.Require(Roles.All); return _repo.GetTypesAsync(ct); }

    public async Task<int> SubmitAsync(LeaveRequestInput input, CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        var employeeId = _user.RequireEmployeeId();
        var types = await _repo.GetTypesAsync(ct);
        var type = types.FirstOrDefault(t => t.LeaveTypeId == input.LeaveTypeId);
        if (type is null) throw new ValidationFailedException(nameof(input.LeaveTypeId), "Unknown leave type.");
        if (input.FromDate is null || input.ToDate is null) { ModelValidator.Validate(input).ThrowIfAny(); return 0; }

        // holidays come from the DB; the day count is computed here so the user can never dictate it
        var holidays = (await _comp.GetHolidaysAsync(input.FromDate.Value, input.ToDate.Value, ct)).Select(h => h.HolidayDate).ToHashSet();
        var days = _calc.CalculateDays(input.FromDate.Value, input.ToDate.Value, input.IsHalfDay, holidays);
        _calc.Validate(input, type, days, DateOnly.FromDateTime(DateTime.UtcNow)).ThrowIfAny();

        var id = await _repo.SubmitAsync(employeeId, input, days, _user.UserId, ct);
        _log.LogInformation("Leave request {RequestId} ({Days} days) submitted by employee {EmployeeId}", id, days, employeeId);
        return id;
    }

    public async Task DecideAsync(int requestId, bool approve, string? remarks, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr, Roles.Manager);
        int? scope = _user.Role == Roles.Manager ? _user.RequireEmployeeId() : null;
        await _repo.DecideAsync(requestId, approve, _user.UserId, _user.EmployeeId, scope, remarks, ct);
        _log.LogInformation("Leave request {RequestId} {Decision} by user {UserId}", requestId, approve ? "approved" : "rejected", _user.UserId);
    }

    public Task CancelAsync(int requestId, CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        int? self = _user.IsInRole(Roles.Admin, Roles.Hr) ? null : _user.RequireEmployeeId();
        return _repo.CancelAsync(requestId, _user.UserId, self, ct);
    }

    public Task<PagedResult<LeaveRequestDto>> GetMyRequestsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        return _repo.GetRequestsAsync(_user.RequireEmployeeId(), null, null, Math.Max(1, page), Math.Clamp(pageSize, 1, 200), ct);
    }

    public Task<PagedResult<LeaveRequestDto>> GetPendingApprovalsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr, Roles.Manager);
        int? mgr = _user.Role == Roles.Manager ? _user.RequireEmployeeId() : null;
        return _repo.GetRequestsAsync(null, LeaveStatus.Pending, mgr, Math.Max(1, page), Math.Clamp(pageSize, 1, 200), ct);
    }

    public Task<IReadOnlyList<LeaveBalanceDto>> GetBalancesAsync(int? employeeId, int year, CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        var target = employeeId ?? _user.RequireEmployeeId();
        if (target != _user.EmployeeId) _user.Require(Roles.Admin, Roles.Hr, Roles.Payroll);
        return _repo.GetBalancesAsync(target, year, ct);
    }

    public Task<IReadOnlyList<LeaveLedgerDto>> GetLedgerAsync(int employeeId, int year, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr);
        return _repo.GetLedgerAsync(employeeId, year, ct);
    }

    public Task<int> RunMonthlyAccrualAsync(int year, int month, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr);
        if (month is < 1 or > 12) throw new ValidationFailedException("month", "Month must be 1-12.");
        return _repo.RunMonthlyAccrualAsync(year, month, _user.UserId, ct);
    }

    /// <summary>Carry-forward of the closing year and January accrual of the next year succeed or fail together.</summary>
    public async Task<(int CarryForward, int JanuaryAccrual)> RunYearEndAsync(int closingYear, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr);
        return await _tx.RunAsync(async () =>
        {
            var carried = await _repo.CarryForwardAsync(closingYear, _user.UserId, ct);
            var accrued = await _repo.RunMonthlyAccrualAsync(closingYear + 1, 1, _user.UserId, ct);
            return (carried, accrued);
        }, ct);
    }

    public Task AdjustAsync(LeaveAdjustInput input, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr);
        ModelValidator.Validate(input).ThrowIfAny();
        return _repo.AdjustAsync(input, _user.UserId, ct);
    }
}
