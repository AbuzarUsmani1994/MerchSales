using Microsoft.Extensions.DependencyInjection;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Catalog.Contracts;
using StockOrder.Modules.Catalog.Infrastructure;
using StockOrder.Modules.Sync.Contracts;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<CatalogDbContext>(
            (sp, options) => new CatalogDbContext(options, sp.GetRequiredService<ICurrentUserContext>()));
        services.AddScoped<ISalesCatalog, SalesCatalog>();
        services.AddScoped<ISyncSource, UomsSyncSource>();
        services.AddScoped<ISyncSource, CategoriesSyncSource>();
        services.AddScoped<ISyncSource, ProductsSyncSource>();
        services.AddScoped<ISyncSource, PriceListsSyncSource>();
        services.AddScoped<ISyncSource, PriceListItemsSyncSource>();
        services.AddScoped<ISyncSource, ProductBranchesSyncSource>();
        return services;
    }
}
