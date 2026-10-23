using Shouldly;
using StockOrder.SharedKernel.Numbers;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>Section 8.7: money rounds to 2 decimals, round-half-away-from-zero.</summary>
public sealed class RoundingTests
{
    [Theory]
    [InlineData(10.005, 10.01)]
    [InlineData(10.004, 10.00)]
    [InlineData(-10.005, -10.01)]
    public void Money_rounds_half_away_from_zero(decimal input, decimal expected)
    {
        Rounding.Money(input).ShouldBe(expected);
    }

    [Fact]
    public void QtyBase_rounds_to_six_decimals()
    {
        Rounding.QtyBase(1.1234567m).ShouldBe(1.123457m);
    }

    [Fact]
    public void DisplayQty_rounds_to_the_uoms_decimal_places()
    {
        Rounding.DisplayQty(1.236m, 2).ShouldBe(1.24m);
        Rounding.DisplayQty(1.236m, 0).ShouldBe(1m);
    }
}
