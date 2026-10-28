using Shouldly;
using StockOrder.Modules.Restaurant.Domain;
using StockOrder.Modules.Sales.Domain;
using StockOrder.SharedKernel.Errors;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>Tables and the kitchen screen (016_RestaurantPack).</summary>
public sealed class RestaurantTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 19, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Cook = Guid.NewGuid();

    private static KitchenTicket NewTicket() =>
        KitchenTicket.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 28), 23, 1,
            SalesChannels.DineIn, "4", "  ", Now, Cook);

    [Fact]
    public void A_ticket_goes_new_preparing_ready_served()
    {
        var ticket = NewTicket();
        ticket.Status.ShouldBe(KitchenTicketStatus.Queued);
        ticket.Notes.ShouldBeNull();

        ticket.Apply(KitchenAction.Start, Cook, Now.AddMinutes(1)).ShouldBeNull();
        ticket.Status.ShouldBe(KitchenTicketStatus.Preparing);
        ticket.StartedAtUtc.ShouldBe(Now.AddMinutes(1));

        ticket.Apply(KitchenAction.Ready, Cook, Now.AddMinutes(9)).ShouldBeNull();
        ticket.Status.ShouldBe(KitchenTicketStatus.Ready);
        ticket.ReadyAtUtc.ShouldBe(Now.AddMinutes(9));

        ticket.Apply(KitchenAction.Serve, Cook, Now.AddMinutes(10)).ShouldBeNull();
        ticket.Status.ShouldBe(KitchenTicketStatus.Served);
        ticket.ServedAtUtc.ShouldBe(Now.AddMinutes(10));
    }

    [Fact]
    public void Back_undoes_one_tap_and_a_finished_ticket_takes_no_more_buttons()
    {
        var ticket = NewTicket();
        ticket.Apply(KitchenAction.Back, Cook, Now)!.Code.ShouldBe(ErrorCodes.InvalidStateTransition);

        ticket.Apply(KitchenAction.Ready, Cook, Now).ShouldBeNull();
        ticket.StartedAtUtc.ShouldBe(Now);
        ticket.Apply(KitchenAction.Back, Cook, Now).ShouldBeNull();
        ticket.Status.ShouldBe(KitchenTicketStatus.Preparing);
        ticket.ReadyAtUtc.ShouldBeNull();
        ticket.Apply(KitchenAction.Back, Cook, Now).ShouldBeNull();
        ticket.Status.ShouldBe(KitchenTicketStatus.Queued);
        ticket.StartedAtUtc.ShouldBeNull();

        ticket.Apply(KitchenAction.Serve, Cook, Now).ShouldBeNull();
        foreach (var action in Enum.GetValues<KitchenAction>())
        {
            ticket.Apply(action, Cook, Now)!.Message.ShouldContain("already served");
        }
    }

    [Fact]
    public void Cancelling_the_order_stops_unserved_tickets_only()
    {
        var open = NewTicket();
        open.Cancel(Now, Cook);
        open.Status.ShouldBe(KitchenTicketStatus.Cancelled);
        open.Apply(KitchenAction.Start, Cook, Now)!.Message.ShouldContain("cancelled");

        var served = NewTicket();
        served.Apply(KitchenAction.Serve, Cook, Now);
        served.Cancel(Now, Cook);
        served.Status.ShouldBe(KitchenTicketStatus.Served);
    }

    [Fact]
    public void A_table_is_taken_until_the_order_is_paid_or_cancelled()
    {
        var table = Guid.NewGuid();
        var paid = NewOrder();
        paid.SeatAt(table, 4, Cook);
        paid.OpenTableId.ShouldBe(table);
        foreach (var status in new[] { SalesOrderStatus.Dispatched, SalesOrderStatus.Delivered, SalesOrderStatus.Invoiced, SalesOrderStatus.Paid })
        {
            paid.MoveTo(status, Cook, Now).ShouldBeNull();
            paid.OpenTableId.ShouldBe(table);
        }

        paid.MoveTo(SalesOrderStatus.Closed, Cook, Now).ShouldBeNull();
        paid.OpenTableId.ShouldBeNull();
        paid.TableId.ShouldBe(table);

        var cancelled = NewOrder();
        cancelled.SeatAt(table, null, Cook);
        cancelled.MoveTo(SalesOrderStatus.Cancelled, Cook, Now, "left").ShouldBeNull();
        cancelled.OpenTableId.ShouldBeNull();
    }

    [Fact]
    public void Rounds_and_the_bill_only_while_confirmed()
    {
        var order = NewOrder();
        order.RequestBill(Cook, Now).ShouldBeNull();
        order.BillRequestedAtUtc.ShouldBe(Now);

        order.StartAmendment(Cook, Now).ShouldBeNull();
        order.Version.ShouldBe(2);
        order.BillRequestedAtUtc.ShouldBeNull();

        order.MoveTo(SalesOrderStatus.Dispatched, Cook, Now);
        order.StartAmendment(Cook, Now)!.Code.ShouldBe(ErrorCodes.InvalidStateTransition);
        order.RequestBill(Cook, Now)!.Code.ShouldBe(ErrorCodes.InvalidStateTransition);
    }

    private static SalesOrder NewOrder()
    {
        var order = SalesOrder.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), SalesChannels.DineIn, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 28),
            null, null, null, null, Cook, Now);
        order.AddLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1000, null, 0, null);
        order.MoveTo(SalesOrderStatus.Submitted, Cook, Now);
        order.MoveTo(SalesOrderStatus.Confirmed, Cook, Now);
        return order;
    }
}
