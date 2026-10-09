using Microsoft.Extensions.Options;
using Payroll.Core.Configuration;
namespace Payroll.Core.Calculation;

/// <summary>Single source of truth for "what is a working day" (used by payroll LOP and leave calculations).</summary>
public sealed class WorkingCalendar
{
    private readonly HashSet<DayOfWeek> _weekend;
    public WorkingCalendar(IOptions<PayrollOptions> options) => _weekend = options.Value.WeekendSet();

    public bool IsWorkingDay(DateOnly date, ISet<DateOnly> holidays) => !_weekend.Contains(date.DayOfWeek) && !holidays.Contains(date);

    public int CountWorkingDays(DateOnly from, DateOnly to, ISet<DateOnly> holidays)
    {
        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (IsWorkingDay(d, holidays)) count++;
        return count;
    }
}
