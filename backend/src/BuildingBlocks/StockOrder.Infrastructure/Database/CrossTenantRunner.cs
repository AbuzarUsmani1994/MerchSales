namespace StockOrder.Infrastructure.Database;

internal sealed class CrossTenantRunner(IBypassConnectionFactory bypassFactory) : ICrossTenantRunner
{
    public async Task<IReadOnlyList<ActiveTenant>> GetActiveTenantsAsync(CancellationToken ct)
    {
        await using var connection = await bypassFactory.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Code, TimeZoneId
            FROM tenancy.Tenant
            WHERE Status = 'Active'
            """;

        var tenants = new List<ActiveTenant>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tenants.Add(new ActiveTenant(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return tenants;
    }
}
