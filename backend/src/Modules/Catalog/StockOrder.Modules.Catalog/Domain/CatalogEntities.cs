using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Catalog.Domain;

/// <summary>Maps catalog.UnitOfMeasure (003_Catalog.sql). Seeded from the industry template.</summary>
public sealed class UnitOfMeasure : TenantEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public byte DecimalPlaces { get; private set; }
    public bool IsActive { get; private set; } = true;

    private UnitOfMeasure()
    {
    }
}

/// <summary>Maps catalog.ProductType. Behaviour: Stocked, NonStock, Service, Kit, Recipe.</summary>
public sealed class ProductType : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string Behaviour { get; private set; } = string.Empty;
    public string DefaultTracking { get; private set; } = "None";
    public string DefaultIssueRule { get; private set; } = "FIFO";
    public bool IsActive { get; private set; } = true;

    private ProductType()
    {
    }
}

/// <summary>Maps catalog.Category. Shared by all branches; the Owner manages them.</summary>
public sealed class Category : AuditedTenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Category()
    {
    }

    public static Category Create(Guid tenantId, Guid id, string name, int sortOrder, DateTime nowUtc, Guid? createdBy) =>
        new(tenantId, id, nowUtc, createdBy) { Name = name, SortOrder = sortOrder };

    private Category(Guid tenantId, Guid id, DateTime nowUtc, Guid? createdBy)
        : base(tenantId, id, nowUtc, createdBy)
    {
    }

    public void Update(string name, int sortOrder, bool isActive, DateTime nowUtc, Guid? updatedBy)
    {
        Name = name;
        SortOrder = sortOrder;
        IsActive = isActive;
        MarkUpdated(nowUtc, updatedBy);
    }
}

/// <summary>
/// Maps catalog.Product. <see cref="OwnerBranchId"/> (014_BranchCatalog.sql): NULL = on the
/// shared menu of every branch; set = sold only at that branch, created by its manager.
/// </summary>
public sealed class Product : AuditedTenantEntity
{
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? ShortName { get; private set; }
    public string? Description { get; private set; }
    public Guid ProductTypeId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid BaseUomId { get; private set; }
    public Guid? SalesUomId { get; private set; }
    public string TrackingMode { get; private set; } = "None";
    public string IssueRule { get; private set; } = "FIFO";
    public bool IsSellable { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    public Guid? OwnerBranchId { get; private set; }

    private Product()
    {
    }

    private Product(Guid tenantId, Guid id, DateTime nowUtc, Guid? createdBy)
        : base(tenantId, id, nowUtc, createdBy)
    {
    }

    /// <summary>The unit the menu is priced and sold in (the base unit unless a sales unit is set).</summary>
    public Guid PricingUomId => SalesUomId ?? BaseUomId;

    public static Product Create(
        Guid tenantId, Guid id, string sku, string name, string? shortName, string? description,
        ProductType type, Guid? categoryId, Guid baseUomId, Guid? ownerBranchId, DateTime nowUtc, Guid? createdBy) =>
        new(tenantId, id, nowUtc, createdBy)
        {
            Sku = sku,
            Name = name,
            ShortName = shortName,
            Description = description,
            ProductTypeId = type.Id,
            CategoryId = categoryId,
            BaseUomId = baseUomId,
            TrackingMode = type.DefaultTracking,
            IssueRule = type.DefaultIssueRule,
            OwnerBranchId = ownerBranchId,
        };

    public void Update(string name, string? shortName, string? description, Guid? categoryId, bool isActive, DateTime nowUtc, Guid? updatedBy)
    {
        Name = name;
        ShortName = shortName;
        Description = description;
        CategoryId = categoryId;
        IsActive = isActive;
        MarkUpdated(nowUtc, updatedBy);
    }
}

/// <summary>Maps catalog.ProductUomConversion. Every product has a factor-1 row for its base unit (section 8.1).</summary>
public sealed class ProductUomConversion : TenantEntity
{
    public Guid ProductId { get; private set; }
    public Guid UomId { get; private set; }
    public decimal FactorToBase { get; private set; }

    private ProductUomConversion()
    {
    }

    public static ProductUomConversion Base(Guid tenantId, Guid id, Guid productId, Guid baseUomId) =>
        new() { TenantId = tenantId, Id = id, ProductId = productId, UomId = baseUomId, FactorToBase = 1m };
}

/// <summary>Maps catalog.PriceList. <see cref="BranchId"/> set = that branch's own prices (014).</summary>
public sealed class PriceList : AuditedTenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string Currency { get; private set; } = "PKR";
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid? BranchId { get; private set; }

    private PriceList()
    {
    }

    private PriceList(Guid tenantId, Guid id, DateTime nowUtc, Guid? createdBy)
        : base(tenantId, id, nowUtc, createdBy)
    {
    }

    public static PriceList ForBranch(Guid tenantId, Guid id, Guid branchId, string name, DateTime nowUtc, Guid? createdBy) =>
        new(tenantId, id, nowUtc, createdBy) { Name = name, BranchId = branchId };
}

/// <summary>Maps catalog.PriceListItem. The menu uses one row per product: MinQuantity 1, no date range.</summary>
public sealed class PriceListItem : TenantEntity
{
    public Guid PriceListId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid UomId { get; private set; }
    public decimal MinQuantity { get; private set; } = 1m;
    public decimal UnitPrice { get; private set; }
    public DateOnly? ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }

    private PriceListItem()
    {
    }

    public static PriceListItem Create(Guid tenantId, Guid id, Guid priceListId, Guid productId, Guid uomId, decimal unitPrice) =>
        new() { TenantId = tenantId, Id = id, PriceListId = priceListId, ProductId = productId, UomId = uomId, UnitPrice = unitPrice };

    public void SetPrice(decimal unitPrice) => UnitPrice = unitPrice;
}

/// <summary>Maps catalog.ProductBranch (014): a product's availability at one branch. No row = available.</summary>
public sealed class ProductBranch : AuditedTenantEntity
{
    public Guid ProductId { get; private set; }
    public Guid BranchId { get; private set; }
    public bool IsAvailable { get; private set; } = true;

    private ProductBranch()
    {
    }

    private ProductBranch(Guid tenantId, Guid id, DateTime nowUtc, Guid? createdBy)
        : base(tenantId, id, nowUtc, createdBy)
    {
    }

    public static ProductBranch Create(Guid tenantId, Guid id, Guid productId, Guid branchId, bool isAvailable, DateTime nowUtc, Guid? createdBy) =>
        new(tenantId, id, nowUtc, createdBy) { ProductId = productId, BranchId = branchId, IsAvailable = isAvailable };

    public void SetAvailable(bool isAvailable, DateTime nowUtc, Guid? updatedBy)
    {
        IsAvailable = isAvailable;
        MarkUpdated(nowUtc, updatedBy);
    }
}
