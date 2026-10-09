using Microsoft.Extensions.Options;
using Payroll.Core.Calculation;
using Payroll.Core.Configuration;
using Payroll.Core.Models;
namespace Payroll.Tests;

internal static class TestData
{
    public static readonly IOptions<PayrollOptions> Options = Microsoft.Extensions.Options.Options.Create(new PayrollOptions());
    public static PayrollCalculator Calculator() => new(new WorkingCalendar(Options), new ProgressiveTaxCalculator(), Options);
    public static LeaveCalculator Leave() => new(new WorkingCalendar(Options), Options);

    /// <summary>March 2026 (FY 2025, 22 working days Mon-Fri, no holidays) with illustrative PF + slab configuration.</summary>
    public static PayrollInputSet March2026()
    {
        var set = new PayrollInputSet { RunId = 1, RunStatus = "Open", Period = new PayrollPeriod { Year = 2026, Month = 3, FinancialYear = 2025 } };
        set.Statutory.Add(new StatutoryRuleDto("PF", "Provident Fund", 12, 12, 15000, "Basic", true));
        set.Slabs.AddRange(new[] { new TaxSlabDto(0, 400000, 0), new TaxSlabDto(400000, 800000, 5), new TaxSlabDto(800000, null, 10) });
        set.TaxParameter = new TaxParameterDto(75000, 4);
        return set;
    }

    public static EmployeePayrollInput Employee(decimal basic = 50000, bool overtime = false, DateOnly? join = null, DateOnly? exit = null) => new()
    {
        EmployeeId = 7, Code = "EMP00007", Name = "Test Employee", DepartmentId = 1, JoinDate = join ?? new DateOnly(2020, 1, 1), ExitDate = exit,
        BasicSalary = basic, StandardHoursPerMonth = 160, OvertimeEligible = overtime
    };
}
