using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StockOrder.Infrastructure.Database;

namespace StockOrder.Infrastructure.Startup;

/// <summary>Backs /health/ready: database reachable and every expected script applied (section 9, 17).</summary>
public sealed class SchemaVersionHealthCheck(IOptions<DatabaseOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await SchemaVersionChecker.EnsureAppliedAsync(options, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (SqlException ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
