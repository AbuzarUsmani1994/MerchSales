using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StockOrder.Infrastructure.Api;
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
    IAccessScope accessScope) : AuthorizationHandler<PermissionRequirement>
{
    private const string PlatformPermissionPrefix = "Platform.";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!currentUser.IsAuthenticated)
        {
            return;
        }

        // Platform.* codes are meaningful only inside the operator tenant. Checked here as well
        // as when role permissions are saved, so a tenant role that somehow holds one still
        // can't reach the cross-tenant endpoints.
        if (requirement.PermissionCode.StartsWith(PlatformPermissionPrefix, StringComparison.Ordinal) &&
            !currentUser.IsPlatformAdmin)
        {
            return;
        }

        var grants = await accessScope.GetGrantsAsync(CancellationToken.None);
        var acceptsBranchGrant = IsBranchScopedEndpoint(context.Resource);

        // PlatformAdmin is an ordinary role whose RolePermission rows happen to include every
        // Platform.* code (section 10) — no special-cased bypass here on purpose.
        if (grants.Any(g => g.Code == requirement.PermissionCode && (g.BranchId is null || acceptsBranchGrant)))
        {
            context.Succeed(requirement);
        }
    }

    private static bool IsBranchScopedEndpoint(object? resource)
    {
        var endpoint = resource switch
        {
            HttpContext httpContext => httpContext.GetEndpoint(),
            Endpoint e => e,
            _ => null,
        };

        return endpoint?.Metadata.GetMetadata<BranchScopedAttribute>() is not null;
    }
}
