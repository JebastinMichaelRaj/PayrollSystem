namespace Payroll.Core.Configuration;

/// <summary>Business-policy settings bound from the "Payroll" section of appsettings.json.</summary>
public sealed class PayrollOptions
{
    public const string Section = "Payroll";
    /// <summary>Month in which the tax/financial year starts (4 = April, 1 = January).</summary>
    public int FinancialYearStartMonth { get; set; } = 4;
    /// <summary>Comma separated weekend days, e.g. "Saturday,Sunday" or "Friday,Saturday".</summary>
    public string WeekendDays { get; set; } = "Saturday,Sunday";
    public decimal MaxOvertimeHoursPerMonth { get; set; } = 60;
    /// <summary>Loan instalments are limited to this percentage of net pay (before loan recovery).</summary>
    public decimal LoanRecoveryCapPercentOfNet { get; set; } = 50;
    /// <summary>When true the user who processed a run cannot approve it.</summary>
    public bool RequireSeparateApprover { get; set; } = true;
    public int LeaveBackdateLimitDays { get; set; } = 30;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int MinPasswordLength { get; set; } = 12;

    public HashSet<DayOfWeek> WeekendSet() =>
        WeekendDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Select(d => Enum.Parse<DayOfWeek>(d, true)).ToHashSet();
}
