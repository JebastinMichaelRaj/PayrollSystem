namespace Payroll.Core.Calculation;

public static class Money
{
    /// <summary>All monetary amounts are rounded to 2 dp, midpoint away from zero (payroll convention).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
