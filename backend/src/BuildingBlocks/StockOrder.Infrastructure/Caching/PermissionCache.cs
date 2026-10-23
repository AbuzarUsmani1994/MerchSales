using Microsoft.Extensions.Caching.Hybrid;

namespace StockOrder.Infrastructure.Caching;

internal sealed class PermissionCache(HybridCache cache) : IPermissionCache
{
    public ValueTask<IReadOnlyList<string>> GetOrCreateAsync(
        Guid tenantId,
        Guid userId,
        Guid securityStamp,
        Func<CancellationToken, ValueTask<IReadOnlyList<string>>> factory,
        CancellationToken ct)
    {
        var key = $"perms:{tenantId:N}:{userId:N}:{securityStamp:N}";
        return cache.GetOrCreateAsync(
            key,
            factory,
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(15) },
            cancellationToken: ct);
    }
}
