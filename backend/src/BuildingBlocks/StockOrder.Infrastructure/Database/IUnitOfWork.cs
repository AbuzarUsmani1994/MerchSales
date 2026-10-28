using Microsoft.Data.SqlClient;

namespace StockOrder.Infrastructure.Database;

/// <summary>
/// One connection, one transaction, scoped to the current request (section 5.4). Every
/// module's DbContext used during the request shares this connection and enlists in this
/// transaction via <see cref="ModuleDbContextExtensions.AddModuleDbContext{TContext}"/>, so
/// commands that touch more than one module's tables are still atomic.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    SqlConnection Connection { get; }
    SqlTransaction? Transaction { get; }
    bool IsInitialized { get; }

    /// <summary>Opens the connection, sets SESSION_CONTEXT('TenantId'), and begins the transaction.</summary>
    Task InitializeAsync(Guid tenantId, CancellationToken ct);

    Task CommitAsync(CancellationToken ct);

    /// <summary>
    /// Runs <paramref name="callback"/> once the transaction has committed, never on rollback:
    /// for telling screens (the kitchen board) about changes that are really saved. A failing
    /// callback is logged and doesn't affect the request.
    /// </summary>
    void OnCommitted(Func<Task> callback);
    Task RollbackAsync(CancellationToken ct);
}
