using Microsoft.Data.SqlClient;

namespace StockOrder.Infrastructure.Database;

internal sealed class TenantLookupService(IBypassConnectionFactory bypassFactory) : ITenantLookupService
{
    public async Task<TenantLookupResult?> FindByCodeAsync(string tenantCode, CancellationToken ct)
    {
        await using var connection = await bypassFactory.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Code, Name, Status, IsPlatform, Industry
            FROM tenancy.Tenant
            WHERE Code = @code
            """;
        command.Parameters.Add(new SqlParameter("@code", tenantCode));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new TenantLookupResult(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.GetString(5));
    }
}
