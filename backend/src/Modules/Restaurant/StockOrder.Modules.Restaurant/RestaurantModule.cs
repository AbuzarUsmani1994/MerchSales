using Microsoft.Extensions.DependencyInjection;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Restaurant.Contracts;
using StockOrder.Modules.Restaurant.Infrastructure;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Restaurant;

/// <summary>Restaurant pack (Build Spec section 18, Phase 2): tables and the kitchen screen.</summary>
public static class RestaurantModule
{
    public static IServiceCollection AddRestaurantModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<RestaurantDbContext>(
            (sp, options) => new RestaurantDbContext(options, sp.GetRequiredService<ICurrentUserContext>()));
        services.AddScoped<IKitchen, Kitchen>();
        services.AddScoped<IDiningTables, DiningTables>();
        services.AddScoped<IKitchenBoardNotifier, KitchenBoardNotifier>();
        services.AddSignalR();
        return services;
    }
}
