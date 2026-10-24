using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Domain;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Identity.Application.Users;

public sealed record UserRoleAssignment(Guid UserRoleId, Guid RoleId, string RoleName, Guid? BranchId);

public sealed record UserSummary(
    Guid Id,
    string Username,
    string DisplayName,
    string? Email,
    string? Phone,
    bool IsActive,
    bool MfaEnabled,
    Guid? DefaultBranchId,
    IReadOnlyList<UserRoleAssignment> Roles,
    bool CanManage);

/// <summary>
/// Users the caller may see: everyone for an all-branches <c>Users.View</c> role; otherwise
/// only users whose roles are all at the caller's branches (users with an all-branch role,
/// such as the Owner, stay hidden), plus role-less users whose default branch is theirs.
/// </summary>
public sealed record GetUsersQuery : IQuery<Result<IReadOnlyList<UserSummary>>>;

public sealed class GetUsersQueryHandler(IdentityDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope)
    : IQueryHandler<GetUsersQuery, Result<IReadOnlyList<UserSummary>>>
{
    public async ValueTask<Result<IReadOnlyList<UserSummary>>> Handle(GetUsersQuery query, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);
        var viewScope = BranchScope.For(grants, "Users.View");
        var canManageAny = grants.Any(g => g.Code == UserAdministrationPolicy.UsersManage);

        var users = await db.Users
            .Where(u => u.TenantId == currentUser.TenantId)
            .OrderBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, u.DisplayName, u.Email, u.Phone, u.IsActive, u.MfaEnabled, u.DefaultBranchId })
            .ToListAsync(ct);

        var assignments = await (
                from ur in db.UserRoles
                join r in db.Roles on new { ur.TenantId, Id = ur.RoleId } equals new { r.TenantId, r.Id }
                where ur.TenantId == currentUser.TenantId
                orderby r.Name
                select new { ur.UserId, Assignment = new UserRoleAssignment(ur.Id, ur.RoleId, r.Name, ur.BranchId) })
            .ToListAsync(ct);
        var assignmentsByUser = assignments.ToLookup(a => a.UserId, a => a.Assignment);

        var heldRolesByUser = await db.GetHeldRolesByUserAsync(currentUser.TenantId, null, ct);

        var result = new List<UserSummary>();
        foreach (var user in users)
        {
            var roles = assignmentsByUser[user.Id].ToList();
            var visible = viewScope.AllBranches ||
                          (roles.Count == 0
                              ? viewScope.Covers(user.DefaultBranchId)
                              : roles.All(r => viewScope.Covers(r.BranchId)));
            if (!visible)
            {
                continue;
            }

            var canManage = canManageAny &&
                            user.Id != currentUser.UserId &&
                            UserAdministrationPolicy.CanManageUser(
                                grants, heldRolesByUser.GetValueOrDefault(user.Id) ?? [], user.DefaultBranchId) is null;

            result.Add(new UserSummary(
                user.Id, user.Username, user.DisplayName, user.Email, user.Phone, user.IsActive, user.MfaEnabled,
                user.DefaultBranchId, roles, canManage));
        }

        return Result.Success<IReadOnlyList<UserSummary>>(result);
    }
}

public sealed record AssignableRole(Guid Id, string Name, string? Description);

/// <summary>Roles the caller may assign at <paramref name="BranchId"/> (NULL = for all branches) — feeds the role picker.</summary>
public sealed record GetAssignableRolesQuery(Guid? BranchId) : IQuery<Result<IReadOnlyList<AssignableRole>>>;

public sealed class GetAssignableRolesQueryHandler(IdentityDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope)
    : IQueryHandler<GetAssignableRolesQuery, Result<IReadOnlyList<AssignableRole>>>
{
    public async ValueTask<Result<IReadOnlyList<AssignableRole>>> Handle(GetAssignableRolesQuery query, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);

        var roles = await db.Roles
            .Where(r => r.TenantId == currentUser.TenantId)
            .OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.Description })
            .ToListAsync(ct);

        var permissionsByRole = (await db.RolePermissions
                .Where(rp => rp.TenantId == currentUser.TenantId)
                .Select(rp => new { rp.RoleId, rp.PermissionCode })
                .ToListAsync(ct))
            .ToLookup(rp => rp.RoleId, rp => rp.PermissionCode);

        var assignable = roles
            .Where(r => UserAdministrationPolicy.CanAssignRole(grants, permissionsByRole[r.Id].ToList(), query.BranchId) is null)
            .Select(r => new AssignableRole(r.Id, r.Name, r.Description))
            .ToList();

        return Result.Success<IReadOnlyList<AssignableRole>>(assignable);
    }
}
