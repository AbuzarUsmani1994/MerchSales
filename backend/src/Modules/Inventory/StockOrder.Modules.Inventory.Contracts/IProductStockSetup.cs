namespace StockOrder.Modules.Inventory.Contracts;

/// <summary>
/// Section 8.2: "When a product is created, create its default batch (BatchNo = 'DEFAULT',
/// IsDefault = 1)." Catalog calls this inside the same request transaction (section 5.4).
/// </summary>
public interface IProductStockSetup
{
    Task CreateDefaultBatchAsync(Guid productId, CancellationToken ct);
}
