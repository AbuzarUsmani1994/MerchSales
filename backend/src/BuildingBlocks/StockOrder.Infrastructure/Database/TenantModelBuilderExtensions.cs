using System.Reflection;
using Microsoft.EntityFrameworkCore;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Entities;

namespace StockOrder.Infrastructure.Database;

public static class TenantModelBuilderExtensions
{
    private static readonly MethodInfo SetTenantFilterMethod = typeof(TenantModelBuilderExtensions)
        .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// EF Core's first guard against cross-tenant reads (section 6); row-level security is
    /// the second. Call from every module DbContext's OnModelCreating.
    /// </summary>
    public static void ApplyTenantQueryFilters(this ModelBuilder modelBuilder, ICurrentUserContext currentUser)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            SetTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(null, [modelBuilder, currentUser]);
        }
    }

    private static void SetTenantFilter<TEntity>(ModelBuilder modelBuilder, ICurrentUserContext currentUser)
        where TEntity : TenantEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == currentUser.TenantId);
    }
}
