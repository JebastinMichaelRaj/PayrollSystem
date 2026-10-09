using System.Security.Claims;
using Payroll.Core.Abstractions;
namespace Payroll.Web.Infrastructure;

public static class AppClaims { public const string EmployeeId = "payroll:employee", MustChange = "payroll:mustchange"; }

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;
    private ClaimsPrincipal? P => _accessor.HttpContext?.User;
    public bool IsAuthenticated => P?.Identity?.IsAuthenticated == true;
    public int UserId => int.TryParse(P?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    public string Username => P?.Identity?.Name ?? "";
    public string Role => P?.FindFirstValue(ClaimTypes.Role) ?? "";
    public int? EmployeeId => int.TryParse(P?.FindFirstValue(AppClaims.EmployeeId), out var id) ? id : null;
    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
