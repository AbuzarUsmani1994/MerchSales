using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Tenancy.Domain;

/// <summary>Maps tenancy.Branch (001_Foundation.sql).</summary>
public sealed class Branch : AuditedTenantEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Branch()
    {
    }

    private Branch(Guid tenantId, Guid id, string code, string name, DateTime nowUtc, Guid? createdBy)
        : base(tenantId, id, nowUtc, createdBy)
    {
        Code = code;
        Name = name;
    }

    public static Branch Create(Guid tenantId, Guid id, string code, string name, DateTime nowUtc, Guid? createdBy) =>
        new(tenantId, id, code, name, nowUtc, createdBy);

    public void Deactivate(DateTime nowUtc, Guid? updatedBy)
    {
        IsActive = false;
        MarkUpdated(nowUtc, updatedBy);
    }
}
