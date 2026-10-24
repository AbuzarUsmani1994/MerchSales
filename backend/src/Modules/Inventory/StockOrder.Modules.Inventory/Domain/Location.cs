using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Inventory.Domain;

/// <summary>Maps inventory.Location (004_Inventory.sql).</summary>
public sealed class Location : AuditedTenantEntity
{
    public Guid WarehouseId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string LocationType { get; private set; } = string.Empty;
    public bool IsSellable { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Location()
    {
    }

    private Location(
        Guid tenantId, Guid id, Guid warehouseId, string code, string name, string locationType, bool isSellable,
        DateTime nowUtc, Guid? createdBy)
        : base(tenantId, id, nowUtc, createdBy)
    {
        WarehouseId = warehouseId;
        Code = code;
        Name = name;
        LocationType = locationType;
        IsSellable = isSellable;
    }

    /// <summary>
    /// The three locations every warehouse gets (section 11) — transfers rely on TRANSIT.
    /// Tenant provisioning writes the same set with raw SQL (TenantProvisioningService).
    /// </summary>
    public static IReadOnlyList<Location> CreateStandardSet(
        Guid tenantId, Func<Guid> newId, Guid warehouseId, DateTime nowUtc, Guid? createdBy) =>
    [
        new(tenantId, newId(), warehouseId, "MAIN", "Main Storage", "Storage", true, nowUtc, createdBy),
        new(tenantId, newId(), warehouseId, "TRANSIT", "In Transit", "InTransit", false, nowUtc, createdBy),
        new(tenantId, newId(), warehouseId, "QUARANTINE", "Quarantine", "Quarantine", false, nowUtc, createdBy),
    ];
}
