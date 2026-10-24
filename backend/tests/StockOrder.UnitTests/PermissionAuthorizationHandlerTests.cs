using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Shouldly;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Identity.Infrastructure.Authorization;
using StockOrder.SharedKernel.Context;
using Xunit;

namespace StockOrder.UnitTests;

public sealed class PermissionAuthorizationHandlerTests
{
    private static readonly Guid LakeCity = Guid.NewGuid();

    [Fact]
    public async Task All_branch_role_satisfies_any_endpoint()
    {
        (await AuthorizeAsync("Settings.Manage", [new("Settings.Manage", null)], branchScopedEndpoint: false)).ShouldBeTrue();
    }

    [Fact]
    public async Task Branch_role_satisfies_only_branch_scoped_endpoints()
    {
        PermissionGrant[] lakeCityManager = [new("Settings.Manage", LakeCity), new("Users.Manage", LakeCity)];

        (await AuthorizeAsync("Settings.Manage", lakeCityManager, branchScopedEndpoint: false)).ShouldBeFalse();
        (await AuthorizeAsync("Users.Manage", lakeCityManager, branchScopedEndpoint: true)).ShouldBeTrue();
    }

    [Fact]
    public async Task Platform_permission_is_refused_outside_the_platform_tenant_even_if_a_role_holds_it()
    {
        PermissionGrant[] grants = [new("Platform.Tenants.Manage", null)];

        (await AuthorizeAsync("Platform.Tenants.Manage", grants, branchScopedEndpoint: false, isPlatformAdmin: false)).ShouldBeFalse();
        (await AuthorizeAsync("Platform.Tenants.Manage", grants, branchScopedEndpoint: false, isPlatformAdmin: true)).ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_permission_fails()
    {
        (await AuthorizeAsync("Users.Manage", [new("Users.View", null)], branchScopedEndpoint: true)).ShouldBeFalse();
    }

    private static async Task<bool> AuthorizeAsync(
        string permissionCode, PermissionGrant[] grants, bool branchScopedEndpoint, bool isPlatformAdmin = false)
    {
        var currentUser = Substitute.For<ICurrentUserContext>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.IsPlatformAdmin.Returns(isPlatformAdmin);

        var accessScope = new FixedAccessScope(grants);

        var metadata = branchScopedEndpoint ? new EndpointMetadataCollection(new BranchScopedAttribute()) : EndpointMetadataCollection.Empty;
        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));

        var requirement = new PermissionRequirement(permissionCode);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), httpContext);

        await new PermissionAuthorizationHandler(currentUser, accessScope).HandleAsync(context);
        return context.HasSucceeded;
    }

    private sealed class FixedAccessScope(IReadOnlyList<PermissionGrant> grants) : IAccessScope
    {
        public ValueTask<IReadOnlyList<PermissionGrant>> GetGrantsAsync(CancellationToken ct) => ValueTask.FromResult(grants);
    }
}
