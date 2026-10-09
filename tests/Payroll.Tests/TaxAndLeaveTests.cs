using Payroll.Core.Calculation;
using Payroll.Core.Models;
using Xunit;
namespace Payroll.Tests;

public class TaxCalculatorTests
{
    private static readonly TaxSlabDto[] Slabs = { new(0, 400000, 0), new(400000, 800000, 5), new(800000, null, 10) };

    [Fact] public void Zero_income_means_zero_tax() => Assert.Equal(0, new ProgressiveTaxCalculator().CalculateMonthlyTax(0, Slabs, new TaxParameterDto(0, 0)));
    [Fact] public void Income_below_first_threshold_is_untaxed() => Assert.Equal(0, new ProgressiveTaxCalculator().CalculateMonthlyTax(30000, Slabs, new TaxParameterDto(0, 0)));

    [Fact]
    public void Tax_spans_multiple_slabs()
    {
        // annual 1,200,000: 400k@5% = 20,000 + 400k@10% = 40,000 => 60,000 / 12 = 5,000
        Assert.Equal(5000m, new ProgressiveTaxCalculator().CalculateMonthlyTax(100000, Slabs, new TaxParameterDto(0, 0)));
    }

    [Fact]
    public void Standard_deduction_and_cess_are_applied()
    {
        // annual 1,200,000 - 100,000 = 1,100,000 => 20,000 + 30,000 = 50,000, +4% cess = 52,000 / 12 = 4,333.33
        Assert.Equal(4333.33m, new ProgressiveTaxCalculator().CalculateMonthlyTax(100000, Slabs, new TaxParameterDto(100000, 4)));
    }
}

public class LeaveCalculatorTests
{
    private static readonly HashSet<DateOnly> NoHolidays = new();
    private static LeaveTypeDto Type(bool half = true, int? max = null) => new(1, "AL", "Annual", 18, "Monthly", 5, true, half, max);

    [Fact] public void Weekend_days_are_free() =>
        Assert.Equal(5, TestData.Leave().CalculateDays(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8), false, NoHolidays));   // Mon..Sun

    [Fact] public void Holidays_are_excluded() =>
        Assert.Equal(4, TestData.Leave().CalculateDays(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), false, new HashSet<DateOnly> { new(2026, 3, 4) }));

    [Fact] public void Half_day_is_point_five_on_a_working_day() =>
        Assert.Equal(0.5m, TestData.Leave().CalculateDays(new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 3), true, NoHolidays));

    [Fact] public void Half_day_on_weekend_is_zero() =>
        Assert.Equal(0m, TestData.Leave().CalculateDays(new DateOnly(2026, 3, 7), new DateOnly(2026, 3, 7), true, NoHolidays));

    [Fact]
    public void Request_spanning_two_years_is_rejected()
    {
        var input = new LeaveRequestInput { LeaveTypeId = 1, FromDate = new DateOnly(2026, 12, 30), ToDate = new DateOnly(2027, 1, 2) };
        Assert.True(TestData.Leave().Validate(input, Type(), 2, new DateOnly(2026, 12, 1)).Any);
    }

    [Fact]
    public void Excessive_back_dating_is_rejected()
    {
        var input = new LeaveRequestInput { LeaveTypeId = 1, FromDate = new DateOnly(2026, 1, 5), ToDate = new DateOnly(2026, 1, 5) };
        Assert.True(TestData.Leave().Validate(input, Type(), 1, new DateOnly(2026, 6, 1)).Any);
    }

    [Fact]
    public void Max_consecutive_days_is_enforced()
    {
        var input = new LeaveRequestInput { LeaveTypeId = 1, FromDate = new DateOnly(2026, 3, 2), ToDate = new DateOnly(2026, 3, 13) };
        Assert.True(TestData.Leave().Validate(input, Type(max: 3), 10, new DateOnly(2026, 3, 1)).Any);
    }

    [Fact]
    public void Valid_request_passes()
    {
        var input = new LeaveRequestInput { LeaveTypeId = 1, FromDate = new DateOnly(2026, 3, 2), ToDate = new DateOnly(2026, 3, 4) };
        Assert.False(TestData.Leave().Validate(input, Type(), 3, new DateOnly(2026, 3, 1)).Any);
    }
}
