using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Contracts;

namespace StockOrder.Modules.Tenancy.Infrastructure;

internal sealed class SettingQueriesAdapter(TenancyDbContext db) : ISettingQueries
{
    public async Task<string?> GetSettingAsync(Guid tenantId, string settingKey, CancellationToken ct) =>
        await db.TenantSettings
            .Where(s => s.TenantId == tenantId && s.SettingKey == settingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
}
