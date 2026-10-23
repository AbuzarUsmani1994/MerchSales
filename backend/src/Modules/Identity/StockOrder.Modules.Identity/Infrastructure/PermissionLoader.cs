using Microsoft.EntityFrameworkCore;

namespace StockOrder.Modules.Identity.Infrastructure;

/// <summary>The factory passed to <see cref="StockOrder.Infrastructure.Caching.IPermissionCache"/> on a cache miss.</summary>
public interface IPermissionLoader
{
    Task<IReadOnlyList<string>> LoadAsync(Guid tenantId, Guid userId, CancellationToken ct);
}

internal sealed class PermissionLoader(IdentityDbContext db) : IPermissionLoader
{
    public async Task<IReadOnlyList<string>> LoadAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var roleIds = db.UserRoles
            .Where(ur => ur.TenantId == tenantId && ur.UserId == userId)
            .Select(ur => ur.RoleId);

        return await db.RolePermissions
            .Where(rp => rp.TenantId == tenantId && roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionCode)
            .Distinct()
            .ToListAsync(ct);
    }
}
