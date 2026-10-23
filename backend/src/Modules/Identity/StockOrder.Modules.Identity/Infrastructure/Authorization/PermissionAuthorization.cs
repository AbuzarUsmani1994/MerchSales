using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockOrder.Infrastructure.Caching;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Identity.Infrastructure.Authorization;

/// <summary>
/// Every endpoint's policy name IS a permission code (section 9: every endpoint has an
/// explicit <c>[Authorize(Policy = "&lt;PermissionCode&gt;")]</c> or <c>[AllowAnonymous]</c>).
/// This provider turns any requested policy name into a requirement checked against the
/// current user's cached permission set, so adding a new permission never needs a new
/// policy registration.
/// </summary>
public sealed class PermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build());

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}

public sealed class PermissionAuthorizationHandler(
    ICurrentUserContext currentUser,
    IPermissionCache permissionCache,
    IdentityDbContext db) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!currentUser.IsAuthenticated)
        {
            return;
        }

        var securityStamp = await db.Users
            .Where(u => u.TenantId == currentUser.TenantId && u.Id == currentUser.UserId)
            .Select(u => u.SecurityStamp)
            .FirstOrDefaultAsync();

        var permissions = await permissionCache.GetOrCreateAsync(
            currentUser.TenantId,
            currentUser.UserId,
            securityStamp,
            async innerCt => (IReadOnlyList<string>)await db.RolePermissions
                .Where(rp => rp.TenantId == currentUser.TenantId &&
                             db.UserRoles.Any(ur => ur.TenantId == currentUser.TenantId &&
                                                     ur.UserId == currentUser.UserId &&
                                                     ur.RoleId == rp.RoleId))
                .Select(rp => rp.PermissionCode)
                .Distinct()
                .ToListAsync(innerCt),
            CancellationToken.None);

        // PlatformAdmin is an ordinary role whose RolePermission rows happen to include every
        // Platform.* code (section 10) — no special-cased bypass here on purpose.
        if (permissions.Contains(requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }
    }
}
