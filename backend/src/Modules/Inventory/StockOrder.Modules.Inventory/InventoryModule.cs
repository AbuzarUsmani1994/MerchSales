using Microsoft.Extensions.DependencyInjection;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Inventory.Contracts;
using StockOrder.Modules.Inventory.Infrastructure;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<InventoryDbContext>(
            (sp, options) => new InventoryDbContext(options, sp.GetRequiredService<ICurrentUserContext>()));

        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<IProductStockSetup, ProductStockSetup>();
        services.AddScoped<IWarehouseQueries, WarehouseQueries>();

        return services;
    }
}
