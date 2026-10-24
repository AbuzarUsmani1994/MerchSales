namespace StockOrder.Modules.Tenancy.Contracts;

/// <summary>Read-only access to tenancy.TenantSetting for other modules (section 5.1).</summary>
public interface ISettingQueries
{
    Task<string?> GetSettingAsync(Guid tenantId, string settingKey, CancellationToken ct);
}
