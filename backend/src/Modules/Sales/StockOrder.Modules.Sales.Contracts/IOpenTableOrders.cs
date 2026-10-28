namespace StockOrder.Modules.Sales.Contracts;

/// <summary>The orders open at a branch's tables, for the restaurant's table map.</summary>
public interface IOpenTableOrders
{
    Task<IReadOnlyList<OpenTableOrder>> ListAsync(Guid branchId, CancellationToken ct);
}

/// <param name="Etag">The order's ETag, for the waiter's next action on it (If-Match).</param>
/// <param name="ServerName">The waiter looking after the table.</param>
public sealed record OpenTableOrder(
    Guid OrderId,
    Guid TableId,
    int? OrderNo,
    string? DocumentNo,
    int? Covers,
    string? ServerName,
    DateTime OpenedAtUtc,
    decimal GrandTotal,
    bool BillRequested,
    string Etag);
