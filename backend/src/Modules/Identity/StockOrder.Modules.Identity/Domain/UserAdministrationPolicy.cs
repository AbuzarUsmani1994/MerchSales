using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;

namespace StockOrder.Modules.Identity.Domain;

/// <summary>One role a user holds, at the branch it was assigned for (NULL = all branches).</summary>
public sealed record HeldRole(Guid? BranchId, IReadOnlyCollection<string> PermissionCodes);

/// <summary>
/// Who may hand out which roles, and to whom (section 10, delegated to branch managers):
/// <list type="bullet">
/// <item>An admin can only act at branches their own <c>Users.Manage</c> role covers. Only an
/// admin whose <c>Users.Manage</c> role is for all branches can assign all-branch roles.</item>
/// <item>No escalation: a role can only be assigned if the admin holds every one of its
/// permissions themselves, at that branch.</item>
/// <item>A branch-scoped admin can't create other admins (roles holding <c>Users.Manage</c> or
/// <c>Roles.Manage</c>) and can't manage users who already are admins. Branch managers are
/// created by a tenant-wide admin (the Owner); they then create their own branch staff.</item>
/// <item>Managing an existing user (reset password, add or remove roles) needs the admin to be
/// able to assign every role that user already holds; a user with no roles is managed by
/// whoever covers their default branch.</item>
/// </list>
/// Returns null when allowed, otherwise the <c>PERMISSION_DENIED</c> error to return.
/// </summary>
public static class UserAdministrationPolicy
{
    public const string UsersManage = "Users.Manage";
    public const string RolesManage = "Roles.Manage";
    public const string DevicesManage = "Devices.Manage";

    private static readonly string[] AdminPermissionCodes = [UsersManage, RolesManage];

    public static Error? CanAssignRole(
        IReadOnlyCollection<PermissionGrant> adminGrants, IReadOnlyCollection<string> rolePermissionCodes, Guid? branchId)
    {
        var adminScope = BranchScope.For(adminGrants, UsersManage);
        if (!adminScope.Covers(branchId))
        {
            return Error.PermissionDenied(branchId is null
                ? "Only an administrator for all branches can assign a role for all branches."
                : "You can only assign roles at branches you manage.");
        }

        if (!adminScope.AllBranches && rolePermissionCodes.Any(AdminPermissionCodes.Contains))
        {
            return Error.PermissionDenied(
                "Branch administrators can't assign roles that manage users or roles; ask an administrator for all branches.");
        }

        var heldAtBranch = PermissionsAt(adminGrants, branchId);
        var missing = rolePermissionCodes.Where(code => !heldAtBranch.Contains(code)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            return Error.PermissionDenied(
                $"You can't assign a role with permissions you don't hold there yourself: {string.Join(", ", missing)}.");
        }

        return null;
    }

    public static Error? CanManageUser(
        IReadOnlyCollection<PermissionGrant> adminGrants, IReadOnlyCollection<HeldRole> targetRoles, Guid? targetDefaultBranchId)
    {
        if (targetRoles.Count == 0)
        {
            return BranchScope.For(adminGrants, UsersManage).Covers(targetDefaultBranchId)
                ? null
                : Error.PermissionDenied("This user belongs to a branch you don't manage.");
        }

        foreach (var role in targetRoles)
        {
            if (CanAssignRole(adminGrants, role.PermissionCodes, role.BranchId) is not null)
            {
                return Error.PermissionDenied("This user holds a role you aren't allowed to manage.");
            }
        }

        return null;
    }

    /// <summary>
    /// Devices (phones/tablets) belong to whoever last signed in on them. An all-branches
    /// <c>Devices.Manage</c> role reaches every device; a branch-scoped one only devices whose
    /// user is staff at the admin's branches: every role at a covered branch, none of them for
    /// all branches, none an admin role. So a branch manager can revoke a cashier's lost phone,
    /// but not the phone of another manager or the Owner.
    /// </summary>
    public static Error? CanManageDevice(
        IReadOnlyCollection<PermissionGrant> adminGrants, IReadOnlyCollection<HeldRole> deviceUserRoles, Guid? deviceUserDefaultBranchId)
    {
        var scope = BranchScope.For(adminGrants, DevicesManage);
        if (scope.AllBranches)
        {
            return null;
        }

        var isBranchStaff = deviceUserRoles.Count == 0
            ? scope.Covers(deviceUserDefaultBranchId)
            : deviceUserRoles.All(r => r.BranchId is not null && scope.Covers(r.BranchId) &&
                                       !r.PermissionCodes.Any(AdminPermissionCodes.Contains));

        return isBranchStaff
            ? null
            : Error.PermissionDenied("You can only manage devices of staff at your own branches.");
    }

    /// <summary>
    /// Role permissions apply at every branch the role is assigned for, so changing them is a
    /// tenant-wide action: the caller must hold, for all branches, every permission the role
    /// has now and every permission it will have.
    /// </summary>
    public static Error? CanSetRolePermissions(
        IReadOnlyCollection<PermissionGrant> adminGrants,
        bool adminHoldsRole,
        IReadOnlyCollection<string> currentPermissionCodes,
        IReadOnlyCollection<string> newPermissionCodes)
    {
        if (adminHoldsRole)
        {
            return Error.PermissionDenied("You can't change the permissions of a role you hold yourself.");
        }

        var tenantWide = PermissionsAt(adminGrants, null);
        var missing = currentPermissionCodes.Concat(newPermissionCodes)
            .Where(code => !tenantWide.Contains(code))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        return missing.Count == 0
            ? null
            : Error.PermissionDenied(
                $"You can only edit roles whose permissions you hold for all branches. Missing: {string.Join(", ", missing)}.");
    }

    /// <summary>Permissions usable at <paramref name="branchId"/>: all-branch grants plus that branch's (NULL: all-branch only).</summary>
    private static HashSet<string> PermissionsAt(IEnumerable<PermissionGrant> grants, Guid? branchId) =>
        grants.Where(g => g.BranchId is null || (branchId is not null && g.BranchId == branchId))
            .Select(g => g.Code)
            .ToHashSet(StringComparer.Ordinal);
}
