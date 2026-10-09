using Microsoft.Extensions.DependencyInjection;
using Payroll.Core.Abstractions;
using Payroll.Data.Infrastructure;
using Payroll.Data.Repositories;
namespace Payroll.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddPayrollData(this IServiceCollection services, Action<DatabaseOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<SqlDatabase>();
        services.AddSingleton<ISqlDatabase>(sp => sp.GetRequiredService<SqlDatabase>());
        services.AddSingleton<ITransactionRunner>(sp => sp.GetRequiredService<SqlDatabase>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<ICompensationRepository, CompensationRepository>();
        services.AddScoped<ILeaveRepository, LeaveRepository>();
        services.AddScoped<IPayrollRepository, PayrollRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        return services;
    }
}
