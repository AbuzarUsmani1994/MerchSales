namespace StockOrder.Modules.Inventory.Contracts;

/// <summary>Read-only warehouse facts for other modules (section 5.1).</summary>
public interface IWarehouseQueries
{
    /// <summary>
    /// The warehouse a branch sells from: its first active warehouse by code. Null when the
    /// branch has none (every branch created through the API gets one).
    /// </summary>
    Task<Guid?> GetSellingWarehouseIdAsync(Guid branchId, CancellationToken ct);
}
