using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Tenancy.Domain;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Tenancy.Infrastructure;

/// <summary>Owns the tenancy schema only (section 5.2). tenancy.Tenant is excluded from the tenant query filter — see note on Tenant.</summary>
public sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options, ICurrentUserContext currentUser)
    : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<TenantFeature> TenantFeatures => Set<TenantFeature>();
    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tenancy");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenancyDbContext).Assembly);

        // Tenant itself has no TenantId column (section 6) — filter it by its own Id instead.
        modelBuilder.Entity<Tenant>().HasQueryFilter(t => t.Id == currentUser.TenantId);

        modelBuilder.ApplyTenantQueryFilters(currentUser);
    }
}
