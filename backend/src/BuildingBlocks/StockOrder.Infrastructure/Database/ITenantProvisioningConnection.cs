using Microsoft.Data.SqlClient;

namespace StockOrder.Infrastructure.Database;

/// <summary>
/// Used only by platform tenant provisioning (section 6, 11): the tenant, its branch,
/// warehouse, owner user and industry template rows are created in one transaction before
/// the new TenantId can be set as a session context value.
/// </summary>
public interface ITenantProvisioningConnection
{
    Task<SqlConnection> OpenAsync(CancellationToken ct);
}
