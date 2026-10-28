namespace StockOrder.Modules.Restaurant.Contracts;

/// <summary>
/// What Sales asks of the kitchen inside its own transaction (section 5.4): each round of an
/// order becomes a kitchen ticket, and cancelling the order takes its tickets off the screen.
/// None of these check the caller's permissions; the calling handler has already.
/// </summary>
public interface IKitchen
{
    /// <summary>Creates the next ticket (round) for the order. Lines kept off the kitchen are left out by the caller.</summary>
    Task<KitchenTicketInfo> SendRoundAsync(KitchenRound round, CancellationToken ct);

    /// <summary>Marks the order's unfinished tickets Cancelled; the kitchen screen shows them crossed out for a few minutes.</summary>
    Task CancelOrderAsync(Guid orderId, Guid branchId, CancellationToken ct);

    /// <summary>
    /// A dine-in table paid: the guests have eaten, so tickets nobody marked served come off the
    /// screen as served. Not for takeaway, where customers often pay before the food is made.
    /// </summary>
    Task CloseTableOrderAsync(Guid orderId, Guid branchId, CancellationToken ct);

    /// <summary>How far the kitchen has got with each order, for the waiter's screens.</summary>
    Task<IReadOnlyDictionary<Guid, KitchenProgress>> GetProgressAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken ct);
}

/// <param name="OrderNo">The short daily number (#23) the kitchen and the customer go by.</param>
/// <param name="TableName">Null for takeaway and counter orders.</param>
public sealed record KitchenRound(
    Guid OrderId,
    Guid BranchId,
    DateOnly BusinessDate,
    int OrderNo,
    string Channel,
    string? TableName,
    string? Notes,
    IReadOnlyList<KitchenRoundLine> Lines);

public sealed record KitchenRoundLine(Guid OrderLineId, Guid ProductId, string ProductName, decimal Quantity, string? Notes);

public sealed record KitchenTicketInfo(Guid TicketId, int RoundNo);

/// <summary>Counts of an order's tickets by status; <see cref="Ready"/> &gt; 0 means food is waiting to be taken out.</summary>
public sealed record KitchenProgress(int Queued, int Preparing, int Ready, int Served);
