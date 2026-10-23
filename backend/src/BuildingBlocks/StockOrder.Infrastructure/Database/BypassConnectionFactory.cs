using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StockOrder.Infrastructure.Database;

internal sealed class BypassConnectionFactory(
    IOptions<DatabaseOptions> options,
    ILogger<BypassConnectionFactory> logger) : IBypassConnectionFactory
{
    public async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        logger.LogWarning("RLS bypass connection opened");

        var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'RlsBypass', @value = 1, @read_only = 1;";
        await command.ExecuteNonQueryAsync(ct);

        return connection;
    }
}
