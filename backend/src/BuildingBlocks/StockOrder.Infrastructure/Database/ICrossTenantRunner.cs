namespace StockOrder.Infrastructure.Database;

/// <summary>
/// Used only by the Worker's scheduled jobs (section 15): list tenants with the bypass
/// connection, then hand each job a normal tenant-scoped connection for the actual work.
/// </summary>
public interface ICrossTenantRunner
{
    Task<IReadOnlyList<ActiveTenant>> GetActiveTenantsAsync(CancellationToken ct);
}

public sealed record ActiveTenant(Guid TenantId, string Code, string TimeZoneId);
