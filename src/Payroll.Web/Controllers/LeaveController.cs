using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Models;
using Payroll.Core.Services;
using Payroll.Web.Infrastructure;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

public sealed class LeaveController : Controller
{
    private readonly ILeaveService _leave;
    private readonly IEmployeeService _employees;
    public LeaveController(ILeaveService leave, IEmployeeService employees) => (_leave, _employees) = (leave, employees);

    public async Task<IActionResult> Index(int? year, int page = 1, CancellationToken ct = default)
    {
        var y = year ?? DateTime.UtcNow.Year;
        return View(new LeaveIndexViewModel
        {
            Year = y, Balances = await _leave.GetBalancesAsync(null, y, ct), Requests = await _leave.GetMyRequestsAsync(page, 25, ct),
            Types = await _leave.GetTypesAsync(ct), Input = new LeaveRequestInput { FromDate = DateOnly.FromDateTime(DateTime.UtcNow), ToDate = DateOnly.FromDateTime(DateTime.UtcNow) }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Apply(LeaveRequestInput input, CancellationToken ct)
    {
        await _leave.SubmitAsync(input, ct);
        TempData["Success"] = "Leave request submitted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        await _leave.CancelAsync(id, ct);
        TempData["Success"] = "Request cancelled.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = Policies.ApproveLeave)]
    public async Task<IActionResult> Pending(int page = 1, CancellationToken ct = default) => View(await _leave.GetPendingApprovalsAsync(page, 25, ct));

    [HttpPost, Authorize(Policy = Policies.ApproveLeave)]
    public async Task<IActionResult> Decide(int id, bool approve, string? remarks, CancellationToken ct)
    {
        await _leave.DecideAsync(id, approve, remarks, ct);
        TempData["Success"] = approve ? "Leave approved." : "Leave rejected.";
        return RedirectToAction(nameof(Pending));
    }

    [Authorize(Policy = Policies.ManageLeaveAdmin)]
    public async Task<IActionResult> Admin(CancellationToken ct) =>
        View(new LeaveAdminViewModel { Employees = (await _employees.GetLookupsAsync(ct)).Employees, Types = await _leave.GetTypesAsync(ct) });

    [HttpPost, Authorize(Policy = Policies.ManageLeaveAdmin)]
    public async Task<IActionResult> Accrue(int year, int month, CancellationToken ct)
    {
        var n = await _leave.RunMonthlyAccrualAsync(year, month, ct);
        TempData["Success"] = $"Accrual posted for {n} employee/leave-type combinations (re-running is safe; duplicates are skipped).";
        return RedirectToAction(nameof(Admin));
    }

    [HttpPost, Authorize(Policy = Policies.ManageLeaveAdmin)]
    public async Task<IActionResult> YearEnd(int closingYear, CancellationToken ct)
    {
        var (carried, accrued) = await _leave.RunYearEndAsync(closingYear, ct);
        TempData["Success"] = $"Year-end complete: {carried} carry-forward balances and {accrued} January accruals posted.";
        return RedirectToAction(nameof(Admin));
    }

    [HttpPost, Authorize(Policy = Policies.ManageLeaveAdmin)]
    public async Task<IActionResult> Adjust([Bind(Prefix = "Adjust")] LeaveAdjustInput input, CancellationToken ct)
    {
        await _leave.AdjustAsync(input, ct);
        TempData["Success"] = "Balance adjusted.";
        return RedirectToAction(nameof(Admin));
    }

    [Authorize(Policy = Policies.ManageLeaveAdmin)]
    public async Task<IActionResult> Ledger(int employeeId, int? year, CancellationToken ct)
    {
        var y = year ?? DateTime.UtcNow.Year;
        return View(new LedgerViewModel { EmployeeId = employeeId, Year = y, Entries = await _leave.GetLedgerAsync(employeeId, y, ct), Balances = await _leave.GetBalancesAsync(employeeId, y, ct) });
    }
}
