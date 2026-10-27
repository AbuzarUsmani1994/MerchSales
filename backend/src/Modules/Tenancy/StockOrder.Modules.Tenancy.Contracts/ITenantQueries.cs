namespace StockOrder.Modules.Tenancy.Contracts;

/// <summary>Tenant facts other modules need, e.g. the time zone that decides business dates (section 2).</summary>
public interface ITenantQueries
{
    /// <summary>The tenant's IANA time zone, e.g. "Asia/Karachi".</summary>
    Task<string> GetTimeZoneIdAsync(Guid tenantId, CancellationToken ct);

    /// <summary>The tenant's industry template ("Restaurant", "Pharma", ...); the phone app's theme follows it.</summary>
    Task<string> GetIndustryAsync(Guid tenantId, CancellationToken ct);

    /// <summary>The company's name, as printed on receipts.</summary>
    Task<string> GetNameAsync(Guid tenantId, CancellationToken ct);
}
