using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Caching;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Identity.Infrastructure.Authorization;

/// <summary>
/// Request-scoped: loads the current user's grants once per request (through the
/// security-stamp-keyed cache) and serves both the authorization handler and handlers.
/// The DbContext is resolved lazily: the authorization handler (and so this class) is also
/// built for anonymous requests, which have no unit of work — an eager DbContext would turn
/// a missing or expired token into a 500 instead of a 401.
/// </summary>
internal sealed class UserAccessScope(
    ICurrentUserContext currentUser,
    IPermissionCache permissionCache,
    IPermissionLoader permissionLoader,
    Func<IdentityDbContext> dbFactory) : IAccessScope
{
    private IReadOnlyList<PermissionGrant>? _grants;

    public async ValueTask<IReadOnlyList<PermissionGrant>> GetGrantsAsync(CancellationToken ct)
    {
        if (_grants is not null)
        {
            return _grants;
        }

        if (!currentUser.IsAuthenticated)
        {
            return _grants = [];
        }

        var securityStamp = await dbFactory().Users
            .Where(u => u.TenantId == currentUser.TenantId && u.Id == currentUser.UserId && u.IsActive)
            .Select(u => (Guid?)u.SecurityStamp)
            .FirstOrDefaultAsync(ct);

        if (securityStamp is null)
        {
            return _grants = [];
        }

        return _grants = await permissionCache.GetOrCreateAsync(
            currentUser.TenantId,
            currentUser.UserId,
            securityStamp.Value,
            async innerCt => await permissionLoader.LoadAsync(currentUser.TenantId, currentUser.UserId, innerCt),
            ct);
    }
}
