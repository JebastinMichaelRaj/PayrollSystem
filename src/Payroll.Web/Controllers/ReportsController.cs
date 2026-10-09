using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Configuration;
using Payroll.Core.Models;
using Payroll.Core.Services;
using Microsoft.Extensions.Options;
using Payroll.Web.Infrastructure;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

[Authorize(Policy = Policies.ViewReports)]
public sealed class ReportsController : Controller
{
    private const int PageSize = 50;
    private readonly IReportService _reports;
    private readonly IPayrollService _payroll;
    private readonly IEmployeeService _employees;
    private readonly PayrollOptions _opt;
    public ReportsController(IReportService reports, IPayrollService payroll, IEmployeeService employees, IOptions<PayrollOptions> opt)
        => (_reports, _payroll, _employees, _opt) = (reports, payroll, employees, opt.Value);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return View(new ReportsIndexViewModel
        {
            Runs = await _payroll.GetRunsAsync(ct), Departments = (await _employees.GetLookupsAsync(ct)).Departments,
            FinancialYear = now.Month >= _opt.FinancialYearStartMonth ? now.Year : now.Year - 1, Year = now.Year
        });
    }

    public async Task<IActionResult> Register(int runId, int page = 1, bool export = false, CancellationToken ct = default)
    {
        var run = await _payroll.GetRunAsync(runId, ct);
        var tables = await _reports.PayrollRegisterAsync(runId, export ? 1 : page, export ? int.MaxValue : PageSize, ct);
        if (export) return new CsvResult(tables, $"payroll-register-{run.PeriodYear}-{run.PeriodMonth:00}.csv");
        return View("Table", new ReportPageViewModel
        {
            Title = "Payroll register", Subtitle = run.Period, Tables = tables,
            Pager = new PagerModel(page, (int)Math.Ceiling((tables[0].TotalRows ?? 0) / (double)PageSize), nameof(Register), "Reports", new Dictionary<string, string?> { ["runId"] = runId.ToString() }),
            ExportUrl = Url.Action(nameof(Register), new { runId, export = true })
        });
    }

    public Task<IActionResult> Department(int runId, bool export = false, CancellationToken ct = default) =>
        Simple(runId, export, "Departmental payroll summary", "department-summary", nameof(Department), _reports.DepartmentSummaryAsync, ct);

    public Task<IActionResult> Statutory(int runId, bool export = false, CancellationToken ct = default) =>
        Simple(runId, export, "Statutory compliance report", "statutory", nameof(Statutory), _reports.StatutoryAsync, ct);

    private async Task<IActionResult> Simple(int runId, bool export, string title, string file, string action,
        Func<int, CancellationToken, Task<IReadOnlyList<ReportTable>>> load, CancellationToken ct)
    {
        var run = await _payroll.GetRunAsync(runId, ct);
        var tables = await load(runId, ct);
        if (export) return new CsvResult(tables, $"{file}-{run.PeriodYear}-{run.PeriodMonth:00}.csv");
        return View("Table", new ReportPageViewModel { Title = title, Subtitle = $"{run.Period} - run is {run.Status}", Tables = tables, ExportUrl = Url.Action(action, new { runId, export = true }) });
    }

    public async Task<IActionResult> Tax(int financialYear, int? departmentId, bool export = false, CancellationToken ct = default)
    {
        var tables = await _reports.TaxAsync(financialYear, departmentId, ct);
        if (export) return new CsvResult(tables, $"tax-report-fy{financialYear}.csv");
        return View("Table", new ReportPageViewModel
        {
            Title = "Tax report", Subtitle = $"Financial year starting {financialYear} (approved and paid runs only)", Tables = tables,
            ExportUrl = Url.Action(nameof(Tax), new { financialYear, departmentId, export = true })
        });
    }

    public async Task<IActionResult> LeaveBalance(int year, int? departmentId, bool export = false, CancellationToken ct = default)
    {
        var tables = await _reports.LeaveBalanceAsync(year, departmentId, ct);
        if (export) return new CsvResult(tables, $"leave-balances-{year}.csv");
        return View("Table", new ReportPageViewModel
        {
            Title = "Leave balance report", Subtitle = year.ToString(), Tables = tables, ExportUrl = Url.Action(nameof(LeaveBalance), new { year, departmentId, export = true })
        });
    }

    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Audit(DateOnly? from, DateOnly? to, string? entity, string? username, int page = 1, bool export = false, CancellationToken ct = default)
    {
        var tables = await _reports.AuditAsync(from, to, entity, username, export ? 1 : page, export ? int.MaxValue : PageSize, ct);
        if (export) return new CsvResult(tables, "audit-trail.csv");
        var q = new Dictionary<string, string?> { ["from"] = from?.ToString("yyyy-MM-dd"), ["to"] = to?.ToString("yyyy-MM-dd"), ["entity"] = entity, ["username"] = username };
        return View("Table", new ReportPageViewModel
        {
            Title = "Audit trail", Tables = tables, Pager = new PagerModel(page, (int)Math.Ceiling((tables[0].TotalRows ?? 0) / (double)PageSize), nameof(Audit), "Reports", q),
            ExportUrl = Url.Action(nameof(Audit), new { from, to, entity, username, export = true })
        });
    }
}
