using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Inventory.Contracts;

namespace StockOrder.Modules.Inventory.Infrastructure;

internal sealed class WarehouseQueries(InventoryDbContext db) : IWarehouseQueries
{
    public async Task<Guid?> GetSellingWarehouseIdAsync(Guid branchId, CancellationToken ct) =>
        await db.Warehouses
            .Where(w => w.BranchId == branchId && w.IsActive)
            .OrderBy(w => w.Code)
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(ct);
}
