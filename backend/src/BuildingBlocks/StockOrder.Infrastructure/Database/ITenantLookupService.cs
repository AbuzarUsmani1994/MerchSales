namespace StockOrder.Infrastructure.Database;

/// <summary>Resolves a tenant by the code typed at login, before any TenantId is known (section 6, 10).</summary>
public interface ITenantLookupService
{
    Task<TenantLookupResult?> FindByCodeAsync(string tenantCode, CancellationToken ct);
}

public sealed record TenantLookupResult(Guid TenantId, string Code, string Name, string Status, bool IsPlatform, string Industry);
