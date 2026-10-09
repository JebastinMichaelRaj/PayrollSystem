using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Payroll.Core.Abstractions;
using Payroll.Core.Domain;
using Payroll.Core.Services;
using Payroll.Web.Infrastructure;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly IAuthService _auth;
    private readonly ICurrentUser _user;
    public AccountController(IAuthService auth, ICurrentUser user) => (_auth, _user) = (auth, user);

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null) { ViewData["ReturnUrl"] = returnUrl; return View(new LoginViewModel()); }

    [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);
        var result = await _auth.LoginAsync(model.Username, model.Password, _user.IpAddress);
        if (!result.Succeeded || result.User is null)
        {
            ModelState.AddModelError("", result.Error ?? "Sign-in failed.");
            model.Password = "";
            return View(model);
        }
        var u = result.User;
        await SignInAsync(u.UserId, u.Username, u.Role, u.EmployeeId, u.MustChangePassword);
        if (u.MustChangePassword) return RedirectToAction(nameof(ChangePassword));
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet] public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        try { await _auth.ChangePasswordAsync(_user.UserId, model.CurrentPassword, model.NewPassword); }
        catch (ValidationFailedException ex)
        {
            foreach (var (field, messages) in ex.Errors) foreach (var m in messages) ModelState.AddModelError(field, m);
            return View(model);
        }
        await SignInAsync(_user.UserId, _user.Username, _user.Role, _user.EmployeeId, false);     // re-issue cookie without the must-change flag
        TempData["Success"] = "Password changed.";
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous] public IActionResult AccessDenied() => View();

    private Task SignInAsync(int userId, string username, string role, int? employeeId, bool mustChange)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Name, username), new(ClaimTypes.Role, role),
            new(AppClaims.MustChange, mustChange ? "1" : "0")
        };
        if (employeeId is { } e) claims.Add(new Claim(AppClaims.EmployeeId, e.ToString()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false });
    }
}
