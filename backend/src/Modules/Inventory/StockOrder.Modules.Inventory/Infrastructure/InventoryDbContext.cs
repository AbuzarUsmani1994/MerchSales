using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Inventory.Domain;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Inventory.Infrastructure;

/// <summary>Owns the inventory schema (section 5.2). Only warehouses and locations are mapped so far.</summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ICurrentUserContext currentUser)
    : DbContext(options)
{
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Location> Locations => Set<Location>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
        modelBuilder.ApplyTenantQueryFilters(currentUser);
    }
}
