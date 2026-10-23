using Microsoft.Extensions.DependencyInjection;
using StockOrder.Modules.Platform.Infrastructure.Bootstrap;
using StockOrder.Modules.Platform.Infrastructure.Provisioning;

namespace StockOrder.Modules.Platform;

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
        services.AddScoped<IBootstrapPlatformService, BootstrapPlatformService>();

        return services;
    }
}
