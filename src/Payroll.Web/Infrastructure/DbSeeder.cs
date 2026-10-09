using Payroll.Core.Abstractions;
using Payroll.Core.Services;
namespace Payroll.Web.Infrastructure;

public static class DbSeeder
{
    /// <summary>Creates the first administrator from configuration (Seed:AdminPassword, ideally an environment variable) when no users exist.</summary>
    public static async Task SeedAdminAsync(IServiceProvider root)
    {
        using var scope = root.CreateScope();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        try
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            if ((await users.ListAsync(default)).Count > 0) return;
            var password = cfg["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(password))
            {
                log.LogWarning("No users exist and Seed:AdminPassword is not set - set it (env var Seed__AdminPassword) and restart to create the first admin.");
                return;
            }
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.CreateBootstrapAdminAsync(cfg["Seed:AdminUsername"] ?? "admin", password, default);
            log.LogInformation("Bootstrap administrator created; a password change will be required at first login.");
        }
        catch (Exception ex) { log.LogError(ex, "Admin seeding skipped (is the database deployed?)"); }
    }
}
