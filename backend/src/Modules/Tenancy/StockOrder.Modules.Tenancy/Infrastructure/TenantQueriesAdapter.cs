using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Contracts;

namespace StockOrder.Modules.Tenancy.Infrastructure;

internal sealed class TenantQueriesAdapter(TenancyDbContext db) : ITenantQueries
{
    public async Task<string> GetTimeZoneIdAsync(Guid tenantId, CancellationToken ct) =>
        await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.TimeZoneId).FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException($"Tenant {tenantId} was not found.");

    public async Task<string> GetNameAsync(Guid tenantId, CancellationToken ct) =>
        await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException($"Tenant {tenantId} was not found.");

    public async Task<string> GetIndustryAsync(Guid tenantId, CancellationToken ct) =>
        await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Industry).FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException($"Tenant {tenantId} was not found.");
}
