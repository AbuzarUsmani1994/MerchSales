using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Identity.Domain;

/// <summary>Maps identity.Role (002_Identity.sql).</summary>
public sealed class Role : AuditedTenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }

    private Role()
    {
    }

    private Role(Guid tenantId, Guid id, string name, string? description, bool isSystem, DateTime nowUtc, Guid? createdBy)
        : base(tenantId, id, nowUtc, createdBy)
    {
        Name = name;
        Description = description;
        IsSystem = isSystem;
    }

    public static Role Create(
        Guid tenantId, Guid id, string name, string? description, bool isSystem, DateTime nowUtc, Guid? createdBy) =>
        new(tenantId, id, name, description, isSystem, nowUtc, createdBy);
}
