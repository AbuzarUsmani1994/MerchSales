namespace StockOrder.Modules.Tenancy.Domain;

/// <summary>Maps tenancy.TenantFeature (001_Foundation.sql). Composite key, no surrogate Id.</summary>
public sealed class TenantFeature
{
    public Guid TenantId { get; private set; }
    public string FeatureKey { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    private TenantFeature()
    {
    }

    public static TenantFeature Create(Guid tenantId, string featureKey, bool isEnabled, DateTime nowUtc) =>
        new() { TenantId = tenantId, FeatureKey = featureKey, IsEnabled = isEnabled, UpdatedAtUtc = nowUtc };

    public void SetEnabled(bool isEnabled, DateTime nowUtc)
    {
        IsEnabled = isEnabled;
        UpdatedAtUtc = nowUtc;
    }
}
