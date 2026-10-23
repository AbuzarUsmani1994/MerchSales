namespace StockOrder.Modules.Tenancy.Domain;

public static class TenantStatus
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Closed = "Closed";
}

public static class TenantIndustry
{
    public const string Restaurant = "Restaurant";
    public const string Pharma = "Pharma";
    public const string Retail = "Retail";
    public const string Platform = "Platform";
}

/// <summary>
/// Maps tenancy.Tenant (001_Foundation.sql). Not a <see cref="SharedKernel.Entities.TenantEntity"/>:
/// this row IS the tenant, so the RLS policy filters it by its own Id, not a separate TenantId column.
/// </summary>
public sealed class Tenant
{
    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Industry { get; private set; } = string.Empty;
    public string BaseCurrency { get; private set; } = "PKR";
    public string TimeZoneId { get; private set; } = "Asia/Karachi";
    public string Status { get; private set; } = TenantStatus.Active;
    public bool IsPlatform { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    private Tenant()
    {
    }

    public static Tenant Create(
        Guid id, string code, string name, string industry, string timeZoneId, bool isPlatform, DateTime nowUtc) =>
        new()
        {
            Id = id,
            Code = code,
            Name = name,
            Industry = industry,
            TimeZoneId = timeZoneId,
            IsPlatform = isPlatform,
            CreatedAtUtc = nowUtc,
        };

    public void Suspend(DateTime nowUtc)
    {
        Status = TenantStatus.Suspended;
        UpdatedAtUtc = nowUtc;
    }

    public void Activate(DateTime nowUtc)
    {
        Status = TenantStatus.Active;
        UpdatedAtUtc = nowUtc;
    }
}
