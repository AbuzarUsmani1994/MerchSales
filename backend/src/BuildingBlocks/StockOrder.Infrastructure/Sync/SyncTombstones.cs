using Dapper;
using StockOrder.Infrastructure.Database;

namespace StockOrder.Infrastructure.Sync;

/// <summary>
/// Section 7: a synced row that is physically deleted leaves a sync.Tombstone, so devices drop
/// their copy on the next pull. Written in the request's transaction, together with the delete.
/// </summary>
public static class SyncTombstones
{
    public static async Task WriteAsync(IUnitOfWork unitOfWork, Guid tenantId, string entity, IEnumerable<Guid> ids, CancellationToken ct)
    {
        foreach (var id in ids)
        {
            await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
                "INSERT sync.Tombstone (TenantId, EntityName, EntityId) VALUES (@tenantId, @entity, @id);",
                new { tenantId, entity, id },
                unitOfWork.Transaction,
                cancellationToken: ct));
        }
    }
}
