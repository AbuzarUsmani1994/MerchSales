using Microsoft.Data.SqlClient;

namespace StockOrder.Infrastructure.Database;

/// <summary>
/// Opens a connection scoped to the current request's tenant (section 6). Every query made
/// through a connection from this factory can only see that tenant's rows, enforced twice:
/// EF Core's global query filters, and SQL Server row-level security reading
/// SESSION_CONTEXT('TenantId').
/// </summary>
public interface ITenantConnectionFactory
{
    /// <summary>Opens a new connection and sets its session context to the given tenant.</summary>
    Task<SqlConnection> OpenAsync(Guid tenantId, CancellationToken ct);
}
