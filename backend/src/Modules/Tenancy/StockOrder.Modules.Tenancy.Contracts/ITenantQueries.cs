namespace StockOrder.Modules.Tenancy.Contracts;

/// <summary>Tenant facts other modules need, e.g. the time zone that decides business dates (section 2).</summary>
public interface ITenantQueries
{
    /// <summary>The tenant's IANA time zone, e.g. "Asia/Karachi".</summary>
    Task<string> GetTimeZoneIdAsync(Guid tenantId, CancellationToken ct);
}
