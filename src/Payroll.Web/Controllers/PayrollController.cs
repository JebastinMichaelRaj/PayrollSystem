using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Services;
using Payroll.Web.Infrastructure;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

[Authorize(Policy = Policies.ViewPayroll)]
public sealed class PayrollController : Controller
{
    private const int PageSize = 50;
    private readonly IPayrollService _payroll;
    private readonly IReportService _reports;
    public PayrollController(IPayrollService payroll, IReportService reports) => (_payroll, _reports) = (payroll, reports);

    public async Task<IActionResult> Index(CancellationToken ct) => View(new PayrollIndexViewModel { Runs = await _payroll.GetRunsAsync(ct) });

    [HttpPost, Authorize(Policy = Policies.ManagePayroll)]
    public async Task<IActionResult> Create(int year, int month, CancellationToken ct)
    {
        var id = await _payroll.CreateRunAsync(year, month, ct);
        return RedirectToAction(nameof(Run), new { id });
    }

    public async Task<IActionResult> Run(int id, int page = 1, CancellationToken ct = default)
    {
        var run = await _payroll.GetRunAsync(id, ct);
        var vm = new PayrollRunViewModel { Run = run };
        if (run.EmployeeCount > 0)
        {
            vm.Register = (await _reports.PayrollRegisterAsync(id, page, PageSize, ct))[0];
            vm.Pager = new PagerModel(page, (int)Math.Ceiling((vm.Register.TotalRows ?? 0) / (double)PageSize), nameof(Run), "Payroll", new Dictionary<string, string?> { ["id"] = id.ToString() });
        }
        if (TempData["Warnings"] is string w) vm.Warnings = w.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        return View(vm);
    }

    [HttpPost, Authorize(Policy = Policies.ManagePayroll)]
    public async Task<IActionResult> Process(int id, CancellationToken ct)
    {
        var r = await _payroll.ProcessRunAsync(id, ct);
        TempData["Success"] = $"Processed {r.Processed} payslips. Gross {Fmt.Money(r.TotalGross)}, net {Fmt.Money(r.TotalNet)}.";
        if (r.Warnings.Count > 0) TempData["Warnings"] = string.Join('\n', r.Warnings.Take(25).Append(r.Warnings.Count > 25 ? $"...and {r.Warnings.Count - 25} more (see logs)." : ""));
        return RedirectToAction(nameof(Run), new { id });
    }

    [HttpPost, Authorize(Policy = Policies.ManagePayroll)]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        await _payroll.ApproveRunAsync(id, ct);
        TempData["Success"] = "Run approved and locked. Loan recoveries have been posted; payslips are now visible to employees.";
        return RedirectToAction(nameof(Run), new { id });
    }

    [HttpPost, Authorize(Policy = Policies.ManagePayroll)]
    public async Task<IActionResult> MarkPaid(int id, CancellationToken ct)
    {
        await _payroll.MarkPaidAsync(id, ct);
        TempData["Success"] = "Run marked as paid.";
        return RedirectToAction(nameof(Run), new { id });
    }
}
