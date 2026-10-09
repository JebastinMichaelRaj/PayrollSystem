using Payroll.Core.Models;
namespace Payroll.Core.Calculation;

public interface ITaxCalculator
{
    /// <summary>Monthly withholding = annual progressive tax on (monthly taxable x 12 - standard deduction), plus cess, divided by 12.</summary>
    decimal CalculateMonthlyTax(decimal monthlyTaxableIncome, IReadOnlyList<TaxSlabDto> slabs, TaxParameterDto parameter);
}

public sealed class ProgressiveTaxCalculator : ITaxCalculator
{
    public decimal CalculateMonthlyTax(decimal monthlyTaxableIncome, IReadOnlyList<TaxSlabDto> slabs, TaxParameterDto parameter)
    {
        if (monthlyTaxableIncome <= 0) return 0;
        var annual = Math.Max(0, monthlyTaxableIncome * 12 - parameter.AnnualStandardDeduction);
        decimal tax = 0;
        foreach (var slab in slabs.OrderBy(s => s.From))
        {
            if (annual <= slab.From) break;
            var upper = slab.To is { } to ? Math.Min(annual, to) : annual;
            tax += (upper - slab.From) * slab.RatePercent / 100m;
        }
        tax += tax * parameter.CessPercent / 100m;
        return Money.Round(tax / 12m);
    }
}
