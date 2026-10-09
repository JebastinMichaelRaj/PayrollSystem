using Microsoft.Extensions.Logging;
using Payroll.Core.Abstractions;
using Payroll.Core.Calculation;
using Payroll.Core.Domain;
using Payroll.Core.Models;
namespace Payroll.Core.Services;

public interface IEmployeeService
{
    Task<EmployeeLookups> GetLookupsAsync(CancellationToken ct = default);
    Task<PagedResult<EmployeeListItem>> SearchAsync(string? search, int? departmentId, bool? isActive, int page, int pageSize, CancellationToken ct = default);
    Task<EmployeeDetail> GetAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(EmployeeInput input, CancellationToken ct = default);
    Task UpdateAsync(EmployeeInput input, CancellationToken ct = default);
    Task TerminateAsync(int id, DateOnly exitDate, CancellationToken ct = default);
}

public sealed class EmployeeService : IEmployeeService
{
    private static readonly string[] EmploymentTypes = { "FullTime", "PartTime", "Contract" };
    private readonly IEmployeeRepository _repo;
    private readonly ICurrentUser _user;
    private readonly ILogger<EmployeeService> _log;
    public EmployeeService(IEmployeeRepository repo, ICurrentUser user, ILogger<EmployeeService> log) => (_repo, _user, _log) = (repo, user, log);

    public Task<EmployeeLookups> GetLookupsAsync(CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        return _repo.GetLookupsAsync(ct);
    }

    public Task<PagedResult<EmployeeListItem>> SearchAsync(string? search, int? departmentId, bool? isActive, int page, int pageSize, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr);
        return _repo.SearchAsync(search?.Trim(), departmentId, isActive, Math.Max(1, page), Math.Clamp(pageSize, 1, 200), ct);
    }

    public async Task<EmployeeDetail> GetAsync(int id, CancellationToken ct = default)
    {
        if (!_user.IsInRole(Roles.Admin, Roles.Hr, Roles.Payroll) && _user.EmployeeId != id) throw new ForbiddenException();
        return await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Employee not found.");
    }

    public async Task<int> CreateAsync(EmployeeInput input, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr);
        Validate(input);
        var id = await _repo.CreateAsync(input, _user.UserId, ct);
        _log.LogInformation("Employee {EmployeeId} created by user {UserId}", id, _user.UserId);
        return id;
    }

    public async Task UpdateAsync(EmployeeInput input, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr);
        Validate(input);
        if (input.RowVersion is null || input.RowVersion.Length == 0) throw new ValidationFailedException("RowVersion", "The record version is missing. Reload the page.");
        await _repo.UpdateAsync(input, _user.UserId, ct);
        _log.LogInformation("Employee {EmployeeId} updated by user {UserId}", input.EmployeeId, _user.UserId);
    }

    public async Task TerminateAsync(int id, DateOnly exitDate, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin, Roles.Hr);
        if (exitDate > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)) throw new ValidationFailedException("exitDate", "Exit date is too far in the future.");
        await _repo.TerminateAsync(id, exitDate, _user.UserId, ct);
        _log.LogInformation("Employee {EmployeeId} terminated effective {ExitDate} by user {UserId}", id, exitDate, _user.UserId);
    }

    private static void Validate(EmployeeInput input)
    {
        var e = ModelValidator.Validate(input);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (input.DateOfBirth is { } dob)
        {
            var age = today.Year - dob.Year - (dob > today.AddYears(-(today.Year - dob.Year)) ? 1 : 0);
            if (age < 16 || age > 80) e.Add(nameof(input.DateOfBirth), "Employee age must be between 16 and 80.");
        }
        if (input.JoinDate is { } j && j > today.AddDays(90)) e.Add(nameof(input.JoinDate), "Join date cannot be more than 90 days in the future.");
        if (!EmploymentTypes.Contains(input.EmploymentType)) e.Add(nameof(input.EmploymentType), "Unknown employment type.");
        if (input.ManagerId is { } m && m == input.EmployeeId && m != 0) e.Add(nameof(input.ManagerId), "An employee cannot manage themselves.");
        e.ThrowIfAny();
    }
}
