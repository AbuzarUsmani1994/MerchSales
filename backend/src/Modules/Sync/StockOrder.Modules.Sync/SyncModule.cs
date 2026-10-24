using Microsoft.Extensions.DependencyInjection;

namespace StockOrder.Modules.Sync;

/// <summary>
/// Orchestrates mobile sync (section 12). Owns no DbContext yet: it reads the sync schema
/// with Dapper on the request connection, and each module registers its own ISyncSource /
/// ISyncCommandHandler implementations.
/// </summary>
public static class SyncModule
{
    public static IServiceCollection AddSyncModule(this IServiceCollection services) => services;
}
