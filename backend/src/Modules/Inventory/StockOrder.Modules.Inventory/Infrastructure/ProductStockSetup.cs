using StockOrder.Modules.Inventory.Contracts;
using StockOrder.Modules.Inventory.Domain;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Inventory.Infrastructure;

internal sealed class ProductStockSetup(
    InventoryDbContext db, ICurrentUserContext currentUser, IClock clock, IIdGenerator idGenerator) : IProductStockSetup
{
    public async Task CreateDefaultBatchAsync(Guid productId, CancellationToken ct)
    {
        db.Batches.Add(Batch.CreateDefault(currentUser.TenantId, idGenerator.NewId(), productId, clock.UtcNow, currentUser.UserId));
        await db.SaveChangesAsync(ct);
    }
}
