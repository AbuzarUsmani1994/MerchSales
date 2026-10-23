namespace StockOrder.Modules.Tenancy.Contracts;

/// <summary>Read-only port other modules use to check which features are on for a tenant (section 11).</summary>
public interface IFeatureQueries
{
    Task<IReadOnlyList<string>> GetEnabledFeatureKeysAsync(Guid tenantId, CancellationToken ct);
}
