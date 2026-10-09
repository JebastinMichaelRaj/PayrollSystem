using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Models;
using Payroll.Core.Services;
using Payroll.Web.Infrastructure;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

[Authorize(Policy = Policies.ManageCompensation)]
public sealed class SalaryController : Controller
{
    private readonly IEmployeeService _employees;
    private readonly ICompensationService _comp;
    public SalaryController(IEmployeeService employees, ICompensationService comp) => (_employees, _comp) = (employees, comp);

    public async Task<IActionResult> Manage(int employeeId, CancellationToken ct) => View(new SalaryManageViewModel
    {
        Employee = await _employees.GetAsync(employeeId, ct),
        History = await _comp.GetSalaryHistoryAsync(employeeId, ct),
        Components = await _comp.GetEmployeeComponentsAsync(employeeId, ct),
        Definitions = await _comp.GetComponentDefsAsync(ct),
        Salary = new SalaryInput { EmployeeId = employeeId, EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow) },
        Assign = new ComponentAssignInput { EmployeeId = employeeId, EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow) }
    });

    [HttpPost]
    public async Task<IActionResult> SetSalary([Bind(Prefix = "Salary")] SalaryInput input, CancellationToken ct)
    {
        await _comp.SetSalaryAsync(input, ct);
        TempData["Success"] = "Salary structure saved.";
        return RedirectToAction(nameof(Manage), new { employeeId = input.EmployeeId });
    }

    [HttpPost]
    public async Task<IActionResult> AssignComponent([Bind(Prefix = "Assign")] ComponentAssignInput input, CancellationToken ct)
    {
        await _comp.AssignComponentAsync(input, ct);
        TempData["Success"] = "Component assigned.";
        return RedirectToAction(nameof(Manage), new { employeeId = input.EmployeeId });
    }

    [HttpPost]
    public async Task<IActionResult> EndComponent(int employeeComponentId, int employeeId, DateOnly endDate, CancellationToken ct)
    {
        await _comp.EndComponentAsync(employeeComponentId, endDate, ct);
        TempData["Success"] = "Component ended.";
        return RedirectToAction(nameof(Manage), new { employeeId });
    }
}
