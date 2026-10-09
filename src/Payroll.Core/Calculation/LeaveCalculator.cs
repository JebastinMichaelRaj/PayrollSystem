using Microsoft.Extensions.Options;
using Payroll.Core.Configuration;
using Payroll.Core.Domain;
using Payroll.Core.Models;
namespace Payroll.Core.Calculation;

public sealed class LeaveCalculator
{
    private readonly WorkingCalendar _calendar;
    private readonly PayrollOptions _opt;
    public LeaveCalculator(WorkingCalendar calendar, IOptions<PayrollOptions> options) => (_calendar, _opt) = (calendar, options.Value);

    /// <summary>Working days consumed by a request (weekends and holidays are free). Half-day = 0.5 on a working day.</summary>
    public decimal CalculateDays(DateOnly from, DateOnly to, bool halfDay, ISet<DateOnly> holidays)
    {
        if (to < from) return 0;
        if (halfDay) return from == to && _calendar.IsWorkingDay(from, holidays) ? 0.5m : 0m;
        return _calendar.CountWorkingDays(from, to, holidays);
    }

    public ValidationErrors Validate(LeaveRequestInput input, LeaveTypeDto type, decimal days, DateOnly today)
    {
        var errors = ModelValidator.Validate(input);
        if (input.FromDate is not { } from || input.ToDate is not { } to) return errors;
        if (to < from) errors.Add(nameof(input.ToDate), "'To' must not be before 'From'.");
        if (from.Year != to.Year) errors.Add(nameof(input.ToDate), "A request must fall within one calendar year. Split it into two requests.");
        if (from < today.AddDays(-_opt.LeaveBackdateLimitDays)) errors.Add(nameof(input.FromDate), $"Leave cannot be back-dated by more than {_opt.LeaveBackdateLimitDays} days.");
        if (input.IsHalfDay && (!type.AllowHalfDay || from != to)) errors.Add(nameof(input.IsHalfDay), "Half-day is only possible for a single day and a leave type that allows it.");
        if (type.MaxConsecutiveDays is { } max && to.DayNumber - from.DayNumber + 1 > max) errors.Add(nameof(input.ToDate), $"{type.Name} is limited to {max} consecutive days.");
        if (days <= 0) errors.Add(nameof(input.FromDate), "The selected dates contain no working days.");
        return errors;
    }
}
