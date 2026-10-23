using NetArchTest.Rules;
using Shouldly;
using StockOrder.Infrastructure.Database;
using Xunit;

namespace StockOrder.ArchitectureTests;

/// <summary>
/// Section 6: the RLS bypass is only legitimate for tenant-by-code lookup at login, the
/// Worker's cross-tenant jobs, platform tenant provisioning, and the mobile version-gate
/// middleware (which by nature reads across every tenant's devices). This test is the
/// documented replacement for making <see cref="IBypassConnectionFactory"/> itself
/// <c>internal</c>, which isn't possible (see the interface's XML doc).
/// </summary>
public sealed class BypassConnectionFactoryTests
{
    private static readonly string[] AllowedConsumers =
    [
        "StockOrder.Infrastructure.Database.IBypassConnectionFactory",
        "StockOrder.Infrastructure.Database.BypassConnectionFactory",
        "StockOrder.Infrastructure.InfrastructureServiceCollectionExtensions", // DI registration only, never calls it
        "StockOrder.Infrastructure.Database.TenantLookupService",
        "StockOrder.Infrastructure.Database.CrossTenantRunner",
        "StockOrder.Infrastructure.Database.TenantProvisioningConnection",
        "StockOrder.Infrastructure.Outbox.OutboxDispatcherService",
        "StockOrder.Infrastructure.Mobile.MobileVersionGateMiddleware",
    ];

    [Fact]
    public void Only_the_approved_allow_list_may_consume_IBypassConnectionFactory()
    {
        var assembly = typeof(IBypassConnectionFactory).Assembly;

        var consumers = Types.InAssembly(assembly)
            .That()
            .HaveDependencyOn(typeof(IBypassConnectionFactory).FullName)
            .GetTypes()
            .Select(t => t.FullName)
            .ToList();

        var unapproved = consumers.Except(AllowedConsumers).ToList();

        unapproved.ShouldBeEmpty(
            $"These types consume IBypassConnectionFactory without being on the approved allow-list " +
            $"(section 6): {string.Join(", ", unapproved)}. Add them to AllowedConsumers only after " +
            "confirming the bypass use is legitimate and logged.");
    }
}
