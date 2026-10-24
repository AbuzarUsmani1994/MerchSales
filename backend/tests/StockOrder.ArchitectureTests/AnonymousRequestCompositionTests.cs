using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StockOrder.Infrastructure;
using StockOrder.Modules.Identity;
using StockOrder.Modules.Tenancy;
using Xunit;

namespace StockOrder.ArchitectureTests;

/// <summary>
/// The authorization middleware builds every <see cref="IAuthorizationHandler"/> even for a
/// request with no (or an expired) token, where UnitOfWorkMiddleware never opened a
/// connection. If a handler takes a DbContext eagerly, that request fails with 500 instead of
/// 401 — and the web app's silent token refresh only reacts to 401.
/// </summary>
public sealed class AnonymousRequestCompositionTests
{
    [Fact]
    public void Authorization_handlers_resolve_without_an_initialized_unit_of_work()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Main"] = "Server=unused;Database=unused",
                ["Files:RootPath"] = Path.GetTempPath(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStockOrderInfrastructure(configuration);
        services.AddTenancyModule();
        services.AddIdentityModule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var handlers = Should.NotThrow(() => scope.ServiceProvider.GetServices<IAuthorizationHandler>().ToList());
        handlers.ShouldNotBeEmpty();
    }
}
