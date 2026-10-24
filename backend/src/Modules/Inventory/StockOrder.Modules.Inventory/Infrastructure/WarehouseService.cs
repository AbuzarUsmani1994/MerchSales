using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Inventory.Contracts;
using StockOrder.Modules.Inventory.Domain;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Inventory.Infrastructure;

internal sealed class WarehouseService(
    InventoryDbContext db, ICurrentUserContext currentUser, IClock clock, IIdGenerator idGenerator) : IWarehouseService
{
    public async Task<Result<Guid>> CreateWarehouseAsync(Guid branchId, string code, string name, CancellationToken ct)
    {
        var codeExists = await db.Warehouses.AnyAsync(w => w.TenantId == currentUser.TenantId && w.Code == code, ct);
        if (codeExists)
        {
            return Result.Failure<Guid>(Error.Conflict(ErrorCodes.Duplicate, $"Warehouse code '{code}' already exists."));
        }

        var now = clock.UtcNow;
        var warehouse = Warehouse.Create(currentUser.TenantId, idGenerator.NewId(), branchId, code, name, now, currentUser.UserId);
        db.Warehouses.Add(warehouse);

        // Saved before the locations: the EF model has no relationship to order the inserts
        // by, and Location's FK needs the warehouse row. Same request transaction either way.
        await db.SaveChangesAsync(ct);

        db.Locations.AddRange(Location.CreateStandardSet(currentUser.TenantId, idGenerator.NewId, warehouse.Id, now, currentUser.UserId));
        await db.SaveChangesAsync(ct);

        return Result.Success(warehouse.Id);
    }
}
