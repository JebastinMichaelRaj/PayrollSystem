using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Abstractions;
using Payroll.Core.Calculation;
using Payroll.Core.Configuration;
using Payroll.Core.Security;
using Payroll.Core.Services;
using Payroll.Data;
using Payroll.Web.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, services, lc) => lc.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext());

// ---- configuration & data access ----
builder.Services.Configure<PayrollOptions>(builder.Configuration.GetSection(PayrollOptions.Section));
builder.Services.AddPayrollData(o => o.ConnectionString = builder.Configuration.GetConnectionString("PayrollDb")
    ?? throw new InvalidOperationException("ConnectionStrings:PayrollDb is not configured."));

// ---- business layer ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<WorkingCalendar>();
builder.Services.AddSingleton<ITaxCalculator, ProgressiveTaxCalculator>();
builder.Services.AddSingleton<IPayrollCalculator, PayrollCalculator>();
builder.Services.AddSingleton<LeaveCalculator>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<ICompensationService, CompensationService>();
builder.Services.AddScoped<ILeaveService, LeaveService>();
builder.Services.AddScoped<IPayrollService, PayrollService>();
builder.Services.AddScoped<IReportService, ReportService>();

// ---- security ----
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.Name = "__Host-payroll";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = true;
});
builder.Services.AddAuthorization(Policies.Register);
builder.Services.AddAntiforgery(o => { o.Cookie.Name = "__Host-payroll-af"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Strict; });
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// ---- MVC ----
builder.Services.AddDistributedMemoryCache();      // replace with Redis/SQL cache when running more than one instance
builder.Services.AddSession(o => { o.Cookie.Name = "__Host-payroll-sess"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.IdleTimeout = TimeSpan.FromMinutes(30); });
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.Filters.Add<ServiceExceptionFilter>();
    o.Filters.Add<RequirePasswordChangeFilter>();
}).AddSessionStateTempDataProvider();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); }
app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

await DbSeeder.SeedAdminAsync(app.Services);
app.Run();

public partial class Program { }
