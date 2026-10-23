using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace StockOrder.Infrastructure.Database;

internal sealed class UnitOfWork(IOptions<DatabaseOptions> options) : IUnitOfWork
{
    private SqlConnection? _connection;
    private SqlTransaction? _transaction;

    public bool IsInitialized => _connection is not null;

    public SqlConnection Connection =>
        _connection ?? throw new InvalidOperationException($"{nameof(UnitOfWork)} was not initialized for this request.");

    public SqlTransaction? Transaction => _transaction;

    public async Task InitializeAsync(Guid tenantId, CancellationToken ct)
    {
        if (_connection is not null)
        {
            return;
        }

        var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(ct);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "EXEC sp_set_session_context @key = N'TenantId', @value = @tenantId, @read_only = 1;";
            command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
            await command.ExecuteNonQueryAsync(ct);
        }

        _connection = connection;
        _transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
