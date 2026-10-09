using Microsoft.AspNetCore.Authorization;
using Payroll.Core.Domain;
namespace Payroll.Web.Infrastructure;

public static class Policies
{
    public const string ManageEmployees = nameof(ManageEmployees);          // Admin, HR
    public const string ManageCompensation = nameof(ManageCompensation);    // Admin, HR, Payroll
    public const string ApproveLeave = nameof(ApproveLeave);                // Admin, HR, Manager
    public const string ManageLeaveAdmin = nameof(ManageLeaveAdmin);        // Admin, HR
    public const string ViewPayroll = nameof(ViewPayroll);                  // Admin, HR, Payroll
    public const string ManagePayroll = nameof(ManagePayroll);              // Admin, Payroll
    public const string ViewReports = nameof(ViewReports);                  // Admin, HR, Payroll
    public const string AdminOnly = nameof(AdminOnly);

    public static void Register(AuthorizationOptions o)
    {
        o.AddPolicy(ManageEmployees, p => p.RequireRole(Roles.Admin, Roles.Hr));
        o.AddPolicy(ManageCompensation, p => p.RequireRole(Roles.Admin, Roles.Hr, Roles.Payroll));
        o.AddPolicy(ApproveLeave, p => p.RequireRole(Roles.Admin, Roles.Hr, Roles.Manager));
        o.AddPolicy(ManageLeaveAdmin, p => p.RequireRole(Roles.Admin, Roles.Hr));
        o.AddPolicy(ViewPayroll, p => p.RequireRole(Roles.Admin, Roles.Hr, Roles.Payroll));
        o.AddPolicy(ManagePayroll, p => p.RequireRole(Roles.Admin, Roles.Payroll));
        o.AddPolicy(ViewReports, p => p.RequireRole(Roles.Admin, Roles.Hr, Roles.Payroll));
        o.AddPolicy(AdminOnly, p => p.RequireRole(Roles.Admin));
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();   // secure by default
    }
}
