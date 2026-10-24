using Shouldly;
using StockOrder.Modules.Sales.Domain;
using StockOrder.SharedKernel.Errors;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>Section 8.6 state machine and section 8.7 line maths.</summary>
public sealed class SalesOrderTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid User = Guid.NewGuid();

    [Theory]
    [InlineData(SalesOrderStatus.Draft, SalesOrderStatus.Submitted)]
    [InlineData(SalesOrderStatus.Submitted, SalesOrderStatus.Confirmed)]
    [InlineData(SalesOrderStatus.Submitted, SalesOrderStatus.OnHold)]
    [InlineData(SalesOrderStatus.OnHold, SalesOrderStatus.Confirmed)]
    [InlineData(SalesOrderStatus.Confirmed, SalesOrderStatus.Dispatched)]
    [InlineData(SalesOrderStatus.Fulfilling, SalesOrderStatus.Dispatched)]
    [InlineData(SalesOrderStatus.Dispatched, SalesOrderStatus.Invoiced)]
    [InlineData(SalesOrderStatus.Delivered, SalesOrderStatus.Invoiced)]
    [InlineData(SalesOrderStatus.Invoiced, SalesOrderStatus.Paid)]
    [InlineData(SalesOrderStatus.Fulfilling, SalesOrderStatus.Cancelled)]
    public void Allowed_transitions(string from, string to) => SalesOrderStatus.CanMove(from, to).ShouldBeTrue();

    [Theory]
    [InlineData(SalesOrderStatus.Draft, SalesOrderStatus.Confirmed)]
    [InlineData(SalesOrderStatus.Confirmed, SalesOrderStatus.Paid)]
    [InlineData(SalesOrderStatus.Dispatched, SalesOrderStatus.Cancelled)]
    [InlineData(SalesOrderStatus.Invoiced, SalesOrderStatus.Cancelled)]
    [InlineData(SalesOrderStatus.Closed, SalesOrderStatus.Cancelled)]
    [InlineData(SalesOrderStatus.Cancelled, SalesOrderStatus.Confirmed)]
    public void Refused_transitions(string from, string to) => SalesOrderStatus.CanMove(from, to).ShouldBeFalse();

    [Fact]
    public void Every_transition_is_recorded_and_bad_ones_are_refused()
    {
        var order = NewOrder();
        order.MoveTo(SalesOrderStatus.Submitted, User, Now).ShouldBeNull();
        order.MoveTo(SalesOrderStatus.Paid, User, Now)!.Code.ShouldBe(ErrorCodes.InvalidStateTransition);
        order.MoveTo(SalesOrderStatus.Confirmed, User, Now).ShouldBeNull();

        order.Status.ShouldBe(SalesOrderStatus.Confirmed);
        order.ConfirmedAtUtc.ShouldBe(Now);
        order.StatusHistory.Select(h => h.ToStatus).ShouldBe([SalesOrderStatus.Draft, SalesOrderStatus.Submitted, SalesOrderStatus.Confirmed]);
    }

    [Fact]
    public void A_paid_order_closes_only_once_delivered()
    {
        var order = NewOrder();
        foreach (var status in new[] { SalesOrderStatus.Submitted, SalesOrderStatus.Confirmed, SalesOrderStatus.Dispatched, SalesOrderStatus.Invoiced, SalesOrderStatus.Paid })
        {
            order.MoveTo(status, User, Now).ShouldBeNull();
        }

        order.MoveTo(SalesOrderStatus.Closed, User, Now)!.Code.ShouldBe(ErrorCodes.InvalidStateTransition);
    }

    [Fact]
    public void Cancelling_keeps_the_reason_and_cancels_lines()
    {
        var order = NewOrder();
        order.AddLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, 100m, null, 0, null);
        order.MoveTo(SalesOrderStatus.Cancelled, User, Now, "Customer left").ShouldBeNull();

        order.CancelNotes.ShouldBe("Customer left");
        order.Lines.ShouldAllBe(l => l.Status == SalesOrderLineStatus.Cancelled);
        order.StatusHistory[^1].Reason.ShouldBe("Customer left");
    }

    [Theory]
    // qty, price, discount %, tax % -> discount, tax, total
    [InlineData(2, 700, 0, 0, 0, 0, 1400)]
    [InlineData(3, 33.335, 0, 0, 0, 0, 100.01)]          // gross 100.005 rounds away from zero
    [InlineData(1, 1000, 10, 0, 100, 0, 900)]
    [InlineData(1, 999.99, 5, 17, 50, 161.5, 1111.49)]    // discount 49.9995 -> 50.00; tax on 949.99 = 161.4983 -> 161.50
    public void Line_maths(decimal qty, decimal price, decimal discountPct, decimal taxPct, decimal discount, decimal tax, decimal total)
    {
        var amounts = LineMath.Compute(qty, price, discountPct, taxPct);
        amounts.DiscountAmount.ShouldBe(discount);
        amounts.TaxAmount.ShouldBe(tax);
        amounts.LineTotal.ShouldBe(total);
    }

    [Fact]
    public void Header_totals_are_sums_of_rounded_lines()
    {
        var order = NewOrder();
        order.AddLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 3, 33.335m, null, 0, null);
        order.AddLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, 0.005m, null, 0, null);

        order.SubTotal.ShouldBe(100.02m);
        order.GrandTotal.ShouldBe(100.02m);
        order.Lines.Select(l => l.LineNumber).ShouldBe([1, 2]);
    }

    private static SalesOrder NewOrder() =>
        SalesOrder.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), SalesChannels.DineIn, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 10, 24), "Table 4", null, null, null, User, Now);
}
