using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StockOrder.Infrastructure.Audit;

namespace StockOrder.Infrastructure.Database;

public static class ModuleDbContextExtensions
{
    /// <summary>
    /// Registers a module's DbContext to share the request's single connection and
    /// transaction (section 5.4), with the audit interceptor attached.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        Func<IServiceProvider, DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        services.AddScoped(sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var auditInterceptor = sp.GetRequiredService<AuditSaveChangesInterceptor>();

            var optionsBuilder = new DbContextOptionsBuilder<TContext>()
                .UseSqlServer(unitOfWork.Connection)
                .AddInterceptors(auditInterceptor)
                .ReplaceService<IModelCacheKeyFactory, PerScopeModelCacheKeyFactory>();

            var context = factory(sp, optionsBuilder.Options);
            sp.GetRequiredService<ScopedDbContexts>().Add(context);

            if (unitOfWork.Transaction is not null)
            {
                context.Database.UseTransaction(unitOfWork.Transaction);
            }

            return context;
        });

        return services;
    }
}
