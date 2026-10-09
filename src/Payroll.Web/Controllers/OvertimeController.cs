using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Abstractions;
using Payroll.Core.Domain;
using Payroll.Core.Models;
using Payroll.Core.Services;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

public sealed class OvertimeController : Controller
{
    private readonly ICompensationService _comp;
    private readonly IEmployeeService _employees;
    private readonly ICurrentUser _user;
    public OvertimeController(ICompensationService comp, IEmployeeService employees, ICurrentUser user) => (_comp, _employees, _user) = (comp, employees, user);

    public async Task<IActionResult> Index(string? status, int page = 1, CancellationToken ct = default)
    {
        var vm = new OvertimeListViewModel
        {
            Status = status, Result = await _comp.ListOvertimeAsync(status, page, 25, ct),
            Input = new OvertimeInput { WorkDate = DateOnly.FromDateTime(DateTime.UtcNow) }
        };
        if (_user.IsInRole(Roles.Admin, Roles.Hr)) vm.Employees = (await _employees.GetLookupsAsync(ct)).Employees;
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Submit(OvertimeInput input, CancellationToken ct)
    {
        await _comp.SubmitOvertimeAsync(input, ct);
        TempData["Success"] = "Overtime submitted for approval.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Decide(int id, bool approve, string? remarks, CancellationToken ct)
    {
        await _comp.DecideOvertimeAsync(id, approve, remarks, ct);
        TempData["Success"] = approve ? "Overtime approved." : "Overtime rejected.";
        return RedirectToAction(nameof(Index), new { status = "Pending" });
    }
}
