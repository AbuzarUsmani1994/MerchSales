using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace StockOrder.ArchitectureTests;

/// <summary>
/// Section 9: "Every endpoint has an explicit [Authorize(Policy = "&lt;PermissionCode&gt;")]
/// or [AllowAnonymous]. An architecture test fails the build if any endpoint has neither."
/// </summary>
public sealed class EndpointAuthorizationTests
{
    private static readonly Assembly[] ModuleApiAssemblies =
    [
        typeof(StockOrder.Modules.Tenancy.TenancyModule).Assembly,
        typeof(StockOrder.Modules.Identity.IdentityModule).Assembly,
        typeof(StockOrder.Modules.Platform.PlatformModule).Assembly,
        typeof(StockOrder.Modules.Inventory.InventoryModule).Assembly,
        typeof(StockOrder.Modules.Sync.SyncModule).Assembly,
        typeof(StockOrder.Modules.Catalog.CatalogModule).Assembly,
    ];

    [Fact]
    public void Every_controller_action_has_an_authorize_policy_or_allow_anonymous()
    {
        var violations = new List<string>();

        foreach (var assembly in ModuleApiAssemblies)
        {
            var controllers = assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

            foreach (var controller in controllers)
            {
                var controllerHasAuthorize = controller.GetCustomAttribute<AuthorizeAttribute>() is not null;
                var controllerHasAllowAnonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

                foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!IsActionMethod(action))
                    {
                        continue;
                    }

                    var actionHasAuthorize = action.GetCustomAttribute<AuthorizeAttribute>() is not null;
                    var actionHasAllowAnonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

                    var hasExplicitPolicy = actionHasAuthorize || controllerHasAuthorize;
                    var isAnonymous = actionHasAllowAnonymous || (controllerHasAllowAnonymous && !actionHasAuthorize);

                    if (!hasExplicitPolicy && !isAnonymous)
                    {
                        violations.Add($"{controller.FullName}.{action.Name}");
                    }

                    if (hasExplicitPolicy)
                    {
                        var policy = action.GetCustomAttribute<AuthorizeAttribute>()?.Policy
                            ?? controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
                        if (string.IsNullOrWhiteSpace(policy))
                        {
                            violations.Add($"{controller.FullName}.{action.Name} (Authorize without a Policy name)");
                        }
                    }
                }
            }
        }

        violations.ShouldBeEmpty(
            $"These controller actions have neither [Authorize(Policy=...)] nor [AllowAnonymous]: {string.Join(", ", violations)}");
    }

    private static bool IsActionMethod(MethodInfo method) =>
        !method.IsSpecialName &&
        method.DeclaringType != typeof(object) &&
        method.GetCustomAttribute<NonActionAttribute>() is null;
}
