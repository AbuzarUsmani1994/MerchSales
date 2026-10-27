using Microsoft.Extensions.DependencyInjection;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Sales.Application;
using StockOrder.Modules.Sales.Infrastructure;
using StockOrder.Modules.Sync.Contracts;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Sales;

public static class SalesModule
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<SalesDbContext>(
            (sp, options) => new SalesDbContext(options, sp.GetRequiredService<ICurrentUserContext>()));
        services.AddScoped<SalesOrderViewBuilder>();
        services.AddScoped<LoyaltyStore>();
        services.AddScoped<LoyaltyCheckout>();
        services.AddScoped<ISyncCommandHandler, CreateSalesOrderSyncHandler>();
        services.AddScoped<ISyncSource, OpenOrdersSyncSource>();
        services.AddScoped<ISyncSource, LoyaltyBranchesSyncSource>();
        return services;
    }
}
