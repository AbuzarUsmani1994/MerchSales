namespace StockOrder.SharedKernel.Numbers;

/// <summary>
/// Central rounding rules (section 8.1, 8.7). Money and quantities are always
/// <see cref="decimal"/>, never <see cref="double"/>.
/// </summary>
public static class Rounding
{
    /// <summary>Money results: 2 decimals, round-half-away-from-zero (section 8.7).</summary>
    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Stock-ledger base quantities: always 6 decimals (section 8.1).</summary>
    public static decimal QtyBase(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    /// <summary>Displayed quantities: rounded to the UoM's own DecimalPlaces.</summary>
    public static decimal DisplayQty(decimal value, int decimalPlaces) =>
        Math.Round(value, decimalPlaces, MidpointRounding.AwayFromZero);

    /// <summary>Unit costs: 6 decimals (decimal(19,6) columns).</summary>
    public static decimal UnitCost(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    /// <summary>Percentages: 4 decimals (decimal(9,4) columns).</summary>
    public static decimal Percent(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
