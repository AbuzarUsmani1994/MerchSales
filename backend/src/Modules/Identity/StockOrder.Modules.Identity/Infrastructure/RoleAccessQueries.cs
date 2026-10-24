using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Domain;

namespace StockOrder.Modules.Identity.Infrastructure;

/// <summary>The role/permission lookups <see cref="UserAdministrationPolicy"/> needs as input.</summary>
internal static class RoleAccessQueries
{
    public static async Task<List<string>> GetRolePermissionCodesAsync(
        this IdentityDbContext db, Guid tenantId, Guid roleId, CancellationToken ct) =>
        await db.RolePermissions
            .Where(rp => rp.TenantId == tenantId && rp.RoleId == roleId)
            .Select(rp => rp.PermissionCode)
            .ToListAsync(ct);

    public static async Task<List<HeldRole>> GetHeldRolesAsync(
        this IdentityDbContext db, Guid tenantId, Guid userId, CancellationToken ct)
    {
        var all = await db.GetHeldRolesByUserAsync(tenantId, [userId], ct);
        return all.TryGetValue(userId, out var roles) ? roles : [];
    }

    public static async Task<Dictionary<Guid, List<HeldRole>>> GetHeldRolesByUserAsync(
        this IdentityDbContext db, Guid tenantId, IReadOnlyCollection<Guid>? userIds, CancellationToken ct)
    {
        var assignments = await db.UserRoles
            .Where(ur => ur.TenantId == tenantId && (userIds == null || userIds.Contains(ur.UserId)))
            .Select(ur => new { ur.UserId, ur.RoleId, ur.BranchId })
            .ToListAsync(ct);

        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        var permissionsByRole = (await db.RolePermissions
                .Where(rp => rp.TenantId == tenantId && roleIds.Contains(rp.RoleId))
                .Select(rp => new { rp.RoleId, rp.PermissionCode })
                .ToListAsync(ct))
            .GroupBy(rp => rp.RoleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(x => x.PermissionCode).ToList());

        return assignments
            .GroupBy(a => a.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(a => new HeldRole(a.BranchId, permissionsByRole.GetValueOrDefault(a.RoleId) ?? [])).ToList());
    }
}
