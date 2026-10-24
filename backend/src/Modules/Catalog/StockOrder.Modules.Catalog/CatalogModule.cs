using Microsoft.Extensions.DependencyInjection;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Catalog.Infrastructure;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<CatalogDbContext>(
            (sp, options) => new CatalogDbContext(options, sp.GetRequiredService<ICurrentUserContext>()));
        return services;
    }
}
