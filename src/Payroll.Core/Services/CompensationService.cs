using Microsoft.Extensions.Logging;
using Payroll.Core.Abstractions;
using Payroll.Core.Calculation;
using Payroll.Core.Domain;
using Payroll.Core.Models;
namespace Payroll.Core.Services;

public interface ICompensationService
{
    Task<IReadOnlyList<SalaryStructureDto>> GetSalaryHistoryAsync(int employeeId, CancellationToken ct = default);
    Task SetSalaryAsync(SalaryInput input, CancellationToken ct = default);
    Task<IReadOnlyList<SalaryComponentDef>> GetComponentDefsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeComponentDto>> GetEmployeeComponentsAsync(int employeeId, CancellationToken ct = default);
    Task AssignComponentAsync(ComponentAssignInput input, CancellationToken ct = default);
    Task EndComponentAsync(int employeeComponentId, DateOnly endDate, CancellationToken ct = default);
    Task<int> SubmitOvertimeAsync(OvertimeInput input, CancellationToken ct = default);
    Task DecideOvertimeAsync(int overtimeId, bool approve, string? remarks, CancellationToken ct = default);
    Task<PagedResult<OvertimeDto>> ListOvertimeAsync(string? status, int page, int pageSize, CancellationToken ct = default);
    Task<int> CreateLoanAsync(LoanInput input, CancellationToken ct = default);
    Task<IReadOnlyList<LoanDto>> ListLoansAsync(string? status, CancellationToken ct = default);
    Task CancelLoanAsync(int loanId, CancellationToken ct = default);
    Task<IReadOnlyList<HolidayDto>> GetHolidaysAsync(int year, CancellationToken ct = default);
    Task UpsertHolidayAsync(DateOnly date, string name, CancellationToken ct = default);
    Task DeleteHolidayAsync(DateOnly date, CancellationToken ct = default);
}

public sealed class CompensationService : ICompensationService
{
    private static readonly string[] Comp = { Roles.Admin, Roles.Hr, Roles.Payroll };
    private readonly ICompensationRepository _repo;
    private readonly ICurrentUser _user;
    private readonly ILogger<CompensationService> _log;
    public CompensationService(ICompensationRepository repo, ICurrentUser user, ILogger<CompensationService> log) => (_repo, _user, _log) = (repo, user, log);

    // ---- salary ----
    public Task<IReadOnlyList<SalaryStructureDto>> GetSalaryHistoryAsync(int employeeId, CancellationToken ct = default)
    { _user.Require(Comp); return _repo.GetSalaryHistoryAsync(employeeId, ct); }

    public async Task SetSalaryAsync(SalaryInput input, CancellationToken ct = default)
    {
        _user.Require(Comp);
        ModelValidator.Validate(input).ThrowIfAny();
        await _repo.SetSalaryAsync(input, _user.UserId, ct);
        _log.LogInformation("Salary structure set for employee {EmployeeId} by user {UserId}", input.EmployeeId, _user.UserId);
    }

    public Task<IReadOnlyList<SalaryComponentDef>> GetComponentDefsAsync(CancellationToken ct = default)
    { _user.Require(Comp); return _repo.GetComponentDefsAsync(ct); }
    public Task<IReadOnlyList<EmployeeComponentDto>> GetEmployeeComponentsAsync(int employeeId, CancellationToken ct = default)
    { _user.Require(Comp); return _repo.GetEmployeeComponentsAsync(employeeId, ct); }

    public async Task AssignComponentAsync(ComponentAssignInput input, CancellationToken ct = default)
    {
        _user.Require(Comp);
        ModelValidator.Validate(input).ThrowIfAny();
        await _repo.AssignComponentAsync(input, _user.UserId, ct);
    }
    public Task EndComponentAsync(int employeeComponentId, DateOnly endDate, CancellationToken ct = default)
    { _user.Require(Comp); return _repo.EndComponentAsync(employeeComponentId, endDate, _user.UserId, ct); }

    // ---- overtime ----
    public async Task<int> SubmitOvertimeAsync(OvertimeInput input, CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        ModelValidator.Validate(input).ThrowIfAny();
        var target = input.EmployeeId ?? _user.RequireEmployeeId();
        if (target != _user.EmployeeId) _user.Require(Roles.Admin, Roles.Hr);       // only HR may log on behalf of others
        return await _repo.SubmitOvertimeAsync(target, input, _user.UserId, ct);
    }

    public async Task DecideOvertimeAsync(int overtimeId, bool approve, string? remarks, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr, Roles.Payroll, Roles.Manager);
        int? scope = _user.Role == Roles.Manager ? _user.RequireEmployeeId() : null;   // managers only decide for their direct reports
        await _repo.DecideOvertimeAsync(overtimeId, approve, _user.UserId, _user.EmployeeId, scope, remarks, ct);
    }

    public Task<PagedResult<OvertimeDto>> ListOvertimeAsync(string? status, int page, int pageSize, CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        int? emp = null, mgr = null;
        if (_user.Role == Roles.Employee) emp = _user.RequireEmployeeId();
        else if (_user.Role == Roles.Manager) mgr = _user.RequireEmployeeId();
        return _repo.ListOvertimeAsync(status, emp, mgr, Math.Max(1, page), Math.Clamp(pageSize, 1, 200), ct);
    }

    // ---- loans ----
    public async Task<int> CreateLoanAsync(LoanInput input, CancellationToken ct = default)
    {
        _user.Require(Comp);
        var e = ModelValidator.Validate(input);
        if (input.Installment > input.Principal) e.Add(nameof(input.Installment), "Instalment cannot exceed the principal.");
        e.ThrowIfAny();
        var id = await _repo.CreateLoanAsync(input, _user.UserId, ct);
        _log.LogInformation("Loan {LoanId} created for employee {EmployeeId} by user {UserId}", id, input.EmployeeId, _user.UserId);
        return id;
    }
    public Task<IReadOnlyList<LoanDto>> ListLoansAsync(string? status, CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        int? emp = _user.IsInRole(Comp) ? null : _user.RequireEmployeeId();
        return _repo.ListLoansAsync(emp, status, ct);
    }
    public Task CancelLoanAsync(int loanId, CancellationToken ct = default)
    { _user.Require(Comp); return _repo.CancelLoanAsync(loanId, _user.UserId, ct); }

    // ---- holidays ----
    public Task<IReadOnlyList<HolidayDto>> GetHolidaysAsync(int year, CancellationToken ct = default)
    { _user.Require(Roles.All); return _repo.GetHolidaysAsync(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), ct); }
    public Task UpsertHolidayAsync(DateOnly date, string name, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new ValidationFailedException("name", "Name is required (max 100 characters).");
        return _repo.UpsertHolidayAsync(date, name.Trim(), _user.UserId, ct);
    }
    public Task DeleteHolidayAsync(DateOnly date, CancellationToken ct = default)
    { _user.Require(Roles.Admin, Roles.Hr); return _repo.DeleteHolidayAsync(date, _user.UserId, ct); }
}
