using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Domain;
using Payroll.Core.Models;
using Payroll.Core.Services;
using Payroll.Web.Infrastructure;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

[Authorize(Policy = Policies.ManageEmployees)]
public sealed class EmployeesController : Controller
{
    private const int PageSize = 25;
    private readonly IEmployeeService _employees;
    private readonly ICompensationService _comp;
    public EmployeesController(IEmployeeService employees, ICompensationService comp) => (_employees, _comp) = (employees, comp);

    public async Task<IActionResult> Index(EmployeeListViewModel q, CancellationToken ct)
    {
        q.Result = await _employees.SearchAsync(q.Search, q.DepartmentId, q.IsActive, q.Page, PageSize, ct);
        q.Lookups = await _employees.GetLookupsAsync(ct);
        return View(q);
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct) =>
        View(new EmployeeDetailsViewModel { Employee = await _employees.GetAsync(id, ct), Salary = await _comp.GetSalaryHistoryAsync(id, ct) });

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct) =>
        View("Form", new EmployeeFormViewModel { Lookups = await _employees.GetLookupsAsync(ct), Input = new EmployeeInput { JoinDate = DateOnly.FromDateTime(DateTime.UtcNow) } });

    [HttpPost]
    public async Task<IActionResult> Create(EmployeeInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await _employees.CreateAsync(input, ct);
                TempData["Success"] = "Employee created. Next, define their salary structure.";
                return RedirectToAction("Manage", "Salary", new { employeeId = id });
            }
            catch (ValidationFailedException ex) { AddErrors(ex); }
        }
        return View("Form", new EmployeeFormViewModel { Input = input, Lookups = await _employees.GetLookupsAsync(ct) });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var e = await _employees.GetAsync(id, ct);
        var input = new EmployeeInput
        {
            EmployeeId = e.EmployeeId, FirstName = e.FirstName, LastName = e.LastName, Email = e.Email, Phone = e.Phone, DateOfBirth = e.DateOfBirth, JoinDate = e.JoinDate,
            DepartmentId = e.DepartmentId, DesignationId = e.DesignationId, ManagerId = e.ManagerId, EmploymentType = e.EmploymentType, TaxIdNumber = e.TaxIdNumber,
            BankName = e.BankName, BankAccountNumber = e.BankAccountNumber, RowVersion = e.RowVersion
        };
        return View("Form", new EmployeeFormViewModel { Input = input, Lookups = await _employees.GetLookupsAsync(ct) });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, EmployeeInput input, CancellationToken ct)
    {
        input.EmployeeId = id;
        if (ModelState.IsValid)
        {
            try
            {
                await _employees.UpdateAsync(input, ct);
                TempData["Success"] = "Employee updated.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (ValidationFailedException ex) { AddErrors(ex); }
        }
        return View("Form", new EmployeeFormViewModel { Input = input, Lookups = await _employees.GetLookupsAsync(ct) });
    }

    [HttpPost]
    public async Task<IActionResult> Terminate(int id, DateOnly exitDate, CancellationToken ct)
    {
        await _employees.TerminateAsync(id, exitDate, ct);
        TempData["Success"] = "Employee marked as left; their login was disabled.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private void AddErrors(ValidationFailedException ex)
    {
        foreach (var (field, messages) in ex.Errors) foreach (var m in messages) ModelState.AddModelError(field, m);
    }
}
