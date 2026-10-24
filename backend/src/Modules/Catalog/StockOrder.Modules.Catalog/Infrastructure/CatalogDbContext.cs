using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Catalog.Domain;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Catalog.Infrastructure;

/// <summary>Owns the catalog schema (section 5.2).</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, ICurrentUserContext currentUser)
    : DbContext(options)
{
    public DbSet<UnitOfMeasure> Units => Set<UnitOfMeasure>();
    public DbSet<ProductType> ProductTypes => Set<ProductType>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUomConversion> Conversions => Set<ProductUomConversion>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();
    public DbSet<ProductBranch> ProductBranches => Set<ProductBranch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        modelBuilder.ApplyTenantQueryFilters(currentUser);
    }
}
