using Microsoft.Extensions.Caching.Hybrid;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Infrastructure.Caching;

internal sealed class PermissionCache(HybridCache cache) : IPermissionCache
{
    public ValueTask<IReadOnlyList<PermissionGrant>> GetOrCreateAsync(
        Guid tenantId,
        Guid userId,
        Guid securityStamp,
        Func<CancellationToken, ValueTask<IReadOnlyList<PermissionGrant>>> factory,
        CancellationToken ct)
    {
        var key = $"grants:{tenantId:N}:{userId:N}:{securityStamp:N}";
        return cache.GetOrCreateAsync(
            key,
            factory,
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(15) },
            cancellationToken: ct);
    }
}
