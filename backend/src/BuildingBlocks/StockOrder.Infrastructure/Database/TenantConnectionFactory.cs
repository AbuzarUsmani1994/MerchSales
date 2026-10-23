using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace StockOrder.Infrastructure.Database;

internal sealed class TenantConnectionFactory(IOptions<DatabaseOptions> options) : ITenantConnectionFactory
{
    public async Task<SqlConnection> OpenAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'TenantId', @value = @tenantId, @read_only = 1;";
        command.Parameters.AddWithValue("@tenantId", tenantId);
        await command.ExecuteNonQueryAsync(ct);

        return connection;
    }
}
