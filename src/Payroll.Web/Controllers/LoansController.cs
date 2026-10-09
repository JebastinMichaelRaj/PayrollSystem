using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Abstractions;
using Payroll.Core.Domain;
using Payroll.Core.Models;
using Payroll.Core.Services;
using Payroll.Web.Infrastructure;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

public sealed class LoansController : Controller
{
    private readonly ICompensationService _comp;
    private readonly IEmployeeService _employees;
    private readonly ICurrentUser _user;
    public LoansController(ICompensationService comp, IEmployeeService employees, ICurrentUser user) => (_comp, _employees, _user) = (comp, employees, user);

    public async Task<IActionResult> Index(string? status, CancellationToken ct)
    {
        var vm = new LoansViewModel
        {
            Status = status, Loans = await _comp.ListLoansAsync(status, ct),
            Input = new LoanInput { StartDate = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(1) }
        };
        if (_user.IsInRole(Roles.Admin, Roles.Hr, Roles.Payroll)) vm.Employees = (await _employees.GetLookupsAsync(ct)).Employees;
        return View(vm);
    }

    [HttpPost, Authorize(Policy = Policies.ManageCompensation)]
    public async Task<IActionResult> Create(LoanInput input, CancellationToken ct)
    {
        await _comp.CreateLoanAsync(input, ct);
        TempData["Success"] = "Loan created; instalments are recovered through payroll.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = Policies.ManageCompensation)]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        await _comp.CancelLoanAsync(id, ct);
        TempData["Success"] = "Loan cancelled.";
        return RedirectToAction(nameof(Index));
    }
}
