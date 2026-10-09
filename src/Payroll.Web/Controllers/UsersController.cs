using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Domain;
using Payroll.Core.Models;
using Payroll.Core.Services;
using Payroll.Web.Infrastructure;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

[Authorize(Policy = Policies.AdminOnly)]
public sealed class UsersController : Controller
{
    private readonly IAuthService _auth;
    private readonly IEmployeeService _employees;
    public UsersController(IAuthService auth, IEmployeeService employees) => (_auth, _employees) = (auth, employees);

    private async Task<UsersViewModel> Build(UserInput? input, CancellationToken ct) =>
        new() { Users = await _auth.ListUsersAsync(ct), Input = input ?? new UserInput(), Employees = (await _employees.GetLookupsAsync(ct)).Employees };

    public async Task<IActionResult> Index(CancellationToken ct) => View(await Build(null, ct));

    [HttpPost]
    public async Task<IActionResult> Create(UserInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await _auth.CreateUserAsync(input, ct);
                TempData["Success"] = "User created. They must change the password at first sign-in.";
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationFailedException ex)
            {
                foreach (var (field, messages) in ex.Errors) foreach (var m in messages) ModelState.AddModelError(field, m);
            }
        }
        input.Password = "";
        return View("Index", await Build(input, ct));
    }

    [HttpPost]
    public async Task<IActionResult> SetActive(int id, bool active, CancellationToken ct)
    {
        await _auth.SetActiveAsync(id, active, ct);
        TempData["Success"] = active ? "User activated." : "User deactivated.";
        return RedirectToAction(nameof(Index));
    }
}
