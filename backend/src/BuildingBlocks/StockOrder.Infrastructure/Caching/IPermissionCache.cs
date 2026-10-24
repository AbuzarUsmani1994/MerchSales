using StockOrder.SharedKernel.Context;

namespace StockOrder.Infrastructure.Caching;

/// <summary>
/// Permission codes are never in the access token (section 10) — they're loaded per request
/// from here, keyed by (tenant, user, security stamp). Changing roles or the password
/// rotates the security stamp, which changes the key, so old cached permissions become
/// unreachable without any explicit invalidation call. Each grant keeps the branch its role
/// was assigned for, so branch-scoped roles (section 6) can be enforced from the cache too.
/// </summary>
public interface IPermissionCache
{
    ValueTask<IReadOnlyList<PermissionGrant>> GetOrCreateAsync(
        Guid tenantId,
        Guid userId,
        Guid securityStamp,
        Func<CancellationToken, ValueTask<IReadOnlyList<PermissionGrant>>> factory,
        CancellationToken ct);
}
