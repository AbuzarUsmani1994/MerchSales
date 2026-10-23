using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Contracts;

namespace StockOrder.Modules.Tenancy.Infrastructure;

internal sealed class FeatureQueriesAdapter(TenancyDbContext db) : IFeatureQueries
{
    public async Task<IReadOnlyList<string>> GetEnabledFeatureKeysAsync(Guid tenantId, CancellationToken ct) =>
        await db.TenantFeatures
            .Where(f => f.TenantId == tenantId && f.IsEnabled)
            .Select(f => f.FeatureKey)
            .ToListAsync(ct);
}
