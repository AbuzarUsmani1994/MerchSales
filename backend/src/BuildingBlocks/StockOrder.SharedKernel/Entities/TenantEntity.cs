namespace StockOrder.SharedKernel.Entities;

/// <summary>
/// Base for every tenant-owned entity, matching the column conventions in
/// database/scripts/README.md: TenantId first, Id is the app-chosen uniqueidentifier
/// primary key, ClusterKey is a database-generated identity never set in code, RowVer is
/// the EF concurrency token mapped to the rowversion column (section 7).
/// </summary>
public abstract class TenantEntity
{
    public Guid TenantId { get; protected set; }
    public Guid Id { get; protected set; }

    /// <summary>Database-generated (bigint IDENTITY). Never set this in code.</summary>
    public long ClusterKey { get; private set; }

    public byte[] RowVer { get; private set; } = [];

    protected TenantEntity(Guid tenantId, Guid id)
    {
        TenantId = tenantId;
        Id = id;
    }

    /// <summary>EF Core materialization constructor.</summary>
    protected TenantEntity()
    {
    }
}

/// <summary>Base for entities with the standard CreatedAtUtc/CreatedBy/UpdatedAtUtc/UpdatedBy audit columns.</summary>
public abstract class AuditedTenantEntity : TenantEntity
{
    public DateTime CreatedAtUtc { get; protected set; }
    public Guid? CreatedBy { get; protected set; }
    public DateTime? UpdatedAtUtc { get; protected set; }
    public Guid? UpdatedBy { get; protected set; }

    protected AuditedTenantEntity(Guid tenantId, Guid id, DateTime createdAtUtc, Guid? createdBy)
        : base(tenantId, id)
    {
        CreatedAtUtc = createdAtUtc;
        CreatedBy = createdBy;
    }

    protected AuditedTenantEntity()
    {
    }

    public void MarkUpdated(DateTime updatedAtUtc, Guid? updatedBy)
    {
        UpdatedAtUtc = updatedAtUtc;
        UpdatedBy = updatedBy;
    }
}
