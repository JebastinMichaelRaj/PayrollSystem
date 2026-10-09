using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace Payroll.Web.Infrastructure;

/// <summary>Users flagged MustChangePassword (e.g. the bootstrap admin) can only reach the password page until they change it.</summary>
public sealed class RequirePasswordChangeFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true || user.FindFirst(AppClaims.MustChange)?.Value != "1") return;
        var controller = context.RouteData.Values["controller"]?.ToString();
        if (string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase)) return;
        context.Result = new RedirectToActionResult("ChangePassword", "Account", null);
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
