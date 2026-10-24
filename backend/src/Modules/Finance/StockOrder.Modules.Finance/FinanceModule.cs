using Microsoft.Extensions.DependencyInjection;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Finance.Contracts;
using StockOrder.Modules.Finance.Infrastructure;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Finance;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<FinanceDbContext>(
            (sp, options) => new FinanceDbContext(options, sp.GetRequiredService<ICurrentUserContext>()));
        services.AddScoped<IFinancePosting, FinancePosting>();
        return services;
    }
}
