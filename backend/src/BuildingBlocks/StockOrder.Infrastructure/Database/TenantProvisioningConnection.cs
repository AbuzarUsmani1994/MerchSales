using Microsoft.Data.SqlClient;

namespace StockOrder.Infrastructure.Database;

internal sealed class TenantProvisioningConnection(IBypassConnectionFactory bypassFactory) : ITenantProvisioningConnection
{
    public Task<SqlConnection> OpenAsync(CancellationToken ct) => bypassFactory.OpenAsync(ct);
}
