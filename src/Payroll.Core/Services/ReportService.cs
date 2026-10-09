using Payroll.Core.Abstractions;
using Payroll.Core.Configuration;
using Payroll.Core.Domain;
using Payroll.Core.Models;
using Microsoft.Extensions.Options;
namespace Payroll.Core.Services;

public interface IReportService
{
    Task<IReadOnlyList<ReportTable>> PayrollRegisterAsync(int runId, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<ReportTable>> DepartmentSummaryAsync(int runId, CancellationToken ct = default);
    Task<IReadOnlyList<ReportTable>> StatutoryAsync(int runId, CancellationToken ct = default);
    Task<IReadOnlyList<ReportTable>> TaxAsync(int financialYear, int? departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<ReportTable>> LeaveBalanceAsync(int year, int? departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<ReportTable>> AuditAsync(DateOnly? from, DateOnly? to, string? entity, string? username, int page, int pageSize, CancellationToken ct = default);
    Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default);
}

public sealed class ReportService : IReportService
{
    private static readonly string[] Finance = { Roles.Admin, Roles.Hr, Roles.Payroll };
    private readonly IReportRepository _repo;
    private readonly ICurrentUser _user;
    private readonly PayrollOptions _opt;
    public ReportService(IReportRepository repo, ICurrentUser user, IOptions<PayrollOptions> opt) => (_repo, _user, _opt) = (repo, user, opt.Value);

    private Task<IReadOnlyList<ReportTable>> Run(ReportKind kind, Dictionary<string, object?> args, CancellationToken ct) => _repo.RunAsync(kind, args, ct);

    public Task<IReadOnlyList<ReportTable>> PayrollRegisterAsync(int runId, int page, int pageSize, CancellationToken ct = default)
    { _user.Require(Finance); return Run(ReportKind.PayrollRegister, new() { ["RunId"] = runId, ["PageNumber"] = Math.Max(1, page), ["PageSize"] = Math.Clamp(pageSize, 1, int.MaxValue) }, ct); }

    public Task<IReadOnlyList<ReportTable>> DepartmentSummaryAsync(int runId, CancellationToken ct = default)
    { _user.Require(Finance); return Run(ReportKind.DepartmentSummary, new() { ["RunId"] = runId }, ct); }

    public Task<IReadOnlyList<ReportTable>> StatutoryAsync(int runId, CancellationToken ct = default)
    { _user.Require(Finance); return Run(ReportKind.Statutory, new() { ["RunId"] = runId }, ct); }

    public Task<IReadOnlyList<ReportTable>> TaxAsync(int financialYear, int? departmentId, CancellationToken ct = default)
    {
        _user.Require(Finance);
        return Run(ReportKind.Tax, new() { ["FinancialYear"] = financialYear, ["FyStartMonth"] = _opt.FinancialYearStartMonth, ["DepartmentId"] = departmentId }, ct);
    }

    public Task<IReadOnlyList<ReportTable>> LeaveBalanceAsync(int year, int? departmentId, CancellationToken ct = default)
    { _user.Require(Finance); return Run(ReportKind.LeaveBalance, new() { ["Year"] = year, ["DepartmentId"] = departmentId }, ct); }

    public Task<IReadOnlyList<ReportTable>> AuditAsync(DateOnly? from, DateOnly? to, string? entity, string? username, int page, int pageSize, CancellationToken ct = default)
    {
        _user.Require(Roles.Admin);
        return Run(ReportKind.Audit, new()
        {
            ["From"] = from, ["To"] = to?.AddDays(1), ["EntityName"] = string.IsNullOrWhiteSpace(entity) ? null : entity.Trim(),
            ["Username"] = string.IsNullOrWhiteSpace(username) ? null : username.Trim(), ["PageNumber"] = Math.Max(1, page), ["PageSize"] = Math.Clamp(pageSize, 1, int.MaxValue)
        }, ct);
    }

    public Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        return _repo.GetDashboardAsync(_user.Role == Roles.Manager ? _user.EmployeeId : null, ct);
    }
}
