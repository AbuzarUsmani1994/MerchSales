using StockOrder.SharedKernel.Numbers;

namespace StockOrder.Modules.Sales.Domain;

public sealed record LineAmounts(decimal Gross, decimal DiscountAmount, decimal TaxAmount, decimal LineTotal);

/// <summary>
/// Section 8.7 line maths, tax-exclusive, each money result rounded to 2 decimals away from zero:
/// gross = qty × price; discount = round(gross × pct/100); tax = round((gross − discount) × rate/100);
/// total = gross − discount + tax.
/// </summary>
public static class LineMath
{
    public static LineAmounts Compute(decimal quantity, decimal unitPrice, decimal discountPercent, decimal taxRatePercent)
    {
        var gross = Rounding.Money(quantity * unitPrice);
        var discount = Rounding.Money(gross * discountPercent / 100m);
        var tax = Rounding.Money((gross - discount) * taxRatePercent / 100m);
        return new LineAmounts(gross, discount, tax, gross - discount + tax);
    }
}
