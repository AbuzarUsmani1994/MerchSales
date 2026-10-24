using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Inventory.Domain;

/// <summary>Maps inventory.Warehouse (004_Inventory.sql).</summary>
public sealed class Warehouse : AuditedTenantEntity
{
    public Guid BranchId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    private Warehouse()
    {
    }

    private Warehouse(Guid tenantId, Guid id, Guid branchId, string code, string name, DateTime nowUtc, Guid? createdBy)
        : base(tenantId, id, nowUtc, createdBy)
    {
        BranchId = branchId;
        Code = code;
        Name = name;
    }

    public static Warehouse Create(
        Guid tenantId, Guid id, Guid branchId, string code, string name, DateTime nowUtc, Guid? createdBy) =>
        new(tenantId, id, branchId, code, name, nowUtc, createdBy);
}
