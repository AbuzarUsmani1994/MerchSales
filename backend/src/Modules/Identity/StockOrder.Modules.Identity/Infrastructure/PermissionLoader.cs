using Microsoft.EntityFrameworkCore;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Identity.Infrastructure;

/// <summary>The factory passed to <see cref="StockOrder.Infrastructure.Caching.IPermissionCache"/> on a cache miss.</summary>
public interface IPermissionLoader
{
    Task<IReadOnlyList<PermissionGrant>> LoadAsync(Guid tenantId, Guid userId, CancellationToken ct);
}

internal sealed class PermissionLoader(Func<IdentityDbContext> dbFactory) : IPermissionLoader
{
    public async Task<IReadOnlyList<PermissionGrant>> LoadAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var db = dbFactory();
        var rows = await (
                from ur in db.UserRoles
                join rp in db.RolePermissions on new { ur.TenantId, ur.RoleId } equals new { rp.TenantId, rp.RoleId }
                where ur.TenantId == tenantId && ur.UserId == userId
                select new { rp.PermissionCode, ur.BranchId })
            .Distinct()
            .ToListAsync(ct);

        return rows.Select(r => new PermissionGrant(r.PermissionCode, r.BranchId)).ToList();
    }
}
