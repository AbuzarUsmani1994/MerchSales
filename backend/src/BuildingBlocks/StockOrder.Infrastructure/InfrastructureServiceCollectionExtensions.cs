using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StockOrder.Infrastructure.Audit;
using StockOrder.Infrastructure.Auth;
using StockOrder.Infrastructure.Caching;
using StockOrder.Infrastructure.Context;
using StockOrder.Infrastructure.Correlation;
using StockOrder.Infrastructure.Database;
using StockOrder.Infrastructure.Files;
using StockOrder.Infrastructure.NumberSeries;
using StockOrder.Infrastructure.Outbox;
using StockOrder.Infrastructure.Startup;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers everything in Phase 0's Infrastructure building block (section 18). Called by both hosts.</summary>
    public static IServiceCollection AddStockOrderInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(options =>
        {
            options.ConnectionString = configuration.GetConnectionString("Main")
                ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:Main.");
        });

        services.Configure<FileStoreOptions>(options =>
        {
            options.RootPath = configuration["Files:RootPath"]
                ?? throw new InvalidOperationException("Missing required configuration: Files:RootPath.");
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, SequentialIdGenerator>();
        services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();

        services.AddScoped<ITenantConnectionFactory, TenantConnectionFactory>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ScopedDbContexts>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<INumberSeriesService, NumberSeriesService>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddSingleton<IBypassConnectionFactory, BypassConnectionFactory>();
        services.AddScoped<ITenantLookupService, TenantLookupService>();
        services.AddScoped<IPublicMenuLinkLookup, PublicMenuLinkLookup>();
        services.AddScoped<ICrossTenantRunner, CrossTenantRunner>();
        services.AddScoped<ITenantProvisioningConnection, TenantProvisioningConnection>();

        services.AddSingleton<IFileStore, NtfsFileStore>();
        services.AddScoped<IAttachmentStore, AttachmentStore>();

        services.Configure<SigningCertificateOptions>(configuration.GetSection(SigningCertificateOptions.SectionName));
        services.AddSingleton<ISigningCertificateProvider, SigningCertificateProvider>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddHybridCache();
        services.AddSingleton<IPermissionCache, PermissionCache>();

        services.AddHealthChecks()
            .AddCheck<SchemaVersionHealthCheck>("database", tags: ["ready"]);

        return services;
    }
}
