using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Inventory.Domain;

/// <summary>Maps inventory.Batch (004_Inventory.sql). Only the default batch is created so far.</summary>
public sealed class Batch : TenantEntity
{
    public const string DefaultBatchNo = "DEFAULT";

    public Guid ProductId { get; private set; }
    public string BatchNo { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }
    public string Status { get; private set; } = "Active";
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }

    private Batch()
    {
    }

    public static Batch CreateDefault(Guid tenantId, Guid id, Guid productId, DateTime nowUtc, Guid? createdBy) => new()
    {
        TenantId = tenantId,
        Id = id,
        ProductId = productId,
        BatchNo = DefaultBatchNo,
        IsDefault = true,
        CreatedAtUtc = nowUtc,
        CreatedBy = createdBy,
    };
}
