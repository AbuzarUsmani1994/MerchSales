namespace StockOrder.Modules.Tenancy.Domain;

/// <summary>Maps tenancy.TenantSetting (001_Foundation.sql). Value is free text or JSON.</summary>
public sealed class TenantSetting
{
    public Guid TenantId { get; private set; }
    public string SettingKey { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    private TenantSetting()
    {
    }

    public static TenantSetting Create(Guid tenantId, string settingKey, string value, DateTime nowUtc, Guid? updatedBy) =>
        new() { TenantId = tenantId, SettingKey = settingKey, Value = value, UpdatedAtUtc = nowUtc, UpdatedBy = updatedBy };

    public void SetValue(string value, DateTime nowUtc, Guid? updatedBy)
    {
        Value = value;
        UpdatedAtUtc = nowUtc;
        UpdatedBy = updatedBy;
    }
}
