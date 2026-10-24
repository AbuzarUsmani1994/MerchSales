using Microsoft.Extensions.DependencyInjection;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Sync.Contracts;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.Modules.Tenancy.Infrastructure;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Tenancy;

public static class TenancyModule
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<TenancyDbContext>(
            (sp, options) => new TenancyDbContext(options, sp.GetRequiredService<ICurrentUserContext>()));

        services.AddScoped<IBranchQueries, BranchQueriesAdapter>();
        services.AddScoped<IFeatureQueries, FeatureQueriesAdapter>();
        services.AddScoped<ISettingQueries, SettingQueriesAdapter>();
        services.AddScoped<ISyncSource, SettingsSyncSource>();
        services.AddScoped<ISyncSource, FeaturesSyncSource>();

        return services;
    }
}
