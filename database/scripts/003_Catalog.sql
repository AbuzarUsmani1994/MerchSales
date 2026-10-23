/*
  003_Catalog.sql
  Units of measure, product types, categories, custom attribute definitions, brands,
  tax codes (rates default to 0 - tax rules are off at launch), products, unit
  conversions, barcodes, components (kits / recipes), price lists.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'003_Catalog')
BEGIN
    PRINT N'003_Catalog already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE catalog.UnitOfMeasure
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Uom_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        Code            nvarchar(20)     NOT NULL,   -- 'kg', 'g', 'pcs', 'box', 'strip', 'tab'
        Name            nvarchar(100)    NOT NULL,
        DecimalPlaces   tinyint          NOT NULL CONSTRAINT DF_Uom_Decimals DEFAULT 0, -- display/rounding rule
        IsActive        bit              NOT NULL CONSTRAINT DF_Uom_IsActive DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Uom_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_UnitOfMeasure PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Uom_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Uom_Code UNIQUE NONCLUSTERED (TenantId, Code),
        CONSTRAINT FK_Uom_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_Uom_Decimals CHECK (DecimalPlaces <= 6)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_UnitOfMeasure ON catalog.UnitOfMeasure (TenantId, ClusterKey);
    CREATE INDEX IX_UnitOfMeasure_Sync ON catalog.UnitOfMeasure (TenantId, RowVer);

    CREATE TABLE catalog.ProductType
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_ProductType_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        Name                nvarchar(100)    NOT NULL,   -- 'Medicine', 'Ingredient', 'Dish'
        Behaviour           varchar(20)      NOT NULL,
        DefaultTracking     varchar(20)      NOT NULL CONSTRAINT DF_ProductType_Tracking DEFAULT 'None',
        DefaultIssueRule    varchar(10)      NOT NULL CONSTRAINT DF_ProductType_Issue DEFAULT 'FIFO',
        IsActive            bit              NOT NULL CONSTRAINT DF_ProductType_IsActive DEFAULT 1,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_ProductType_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_ProductType PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_ProductType_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_ProductType_Name UNIQUE NONCLUSTERED (TenantId, Name),
        CONSTRAINT FK_ProductType_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_ProductType_Behaviour CHECK (Behaviour IN ('Stocked','NonStock','Service','Kit','Recipe')),
        CONSTRAINT CK_ProductType_Tracking CHECK (DefaultTracking IN ('None','Batch','BatchExpiry','Serial')),
        CONSTRAINT CK_ProductType_Issue CHECK (DefaultIssueRule IN ('FIFO','FEFO','Manual'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_ProductType ON catalog.ProductType (TenantId, ClusterKey);

    -- Custom field definitions. EntityType says which entity the field belongs to.
    CREATE TABLE catalog.AttributeDefinition
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_AttrDef_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        EntityType          varchar(30)      NOT NULL CONSTRAINT DF_AttrDef_Entity DEFAULT 'Product',
        Code                varchar(60)      NOT NULL,   -- JSON property name, e.g. 'strength'
        Label               nvarchar(100)    NOT NULL,
        DataType            varchar(20)      NOT NULL,
        OptionsJson         nvarchar(max)    NULL,       -- for List/MultiList: ["Tablet","Syrup"]
        IsRequired          bit              NOT NULL CONSTRAINT DF_AttrDef_Required DEFAULT 0,
        IsSearchable        bit              NOT NULL CONSTRAINT DF_AttrDef_Searchable DEFAULT 0,
        ShowInApp           bit              NOT NULL CONSTRAINT DF_AttrDef_ShowInApp DEFAULT 1,
        IsVariantAttribute  bit              NOT NULL CONSTRAINT DF_AttrDef_Variant DEFAULT 0,
        SortOrder           int              NOT NULL CONSTRAINT DF_AttrDef_Sort DEFAULT 0,
        IsActive            bit              NOT NULL CONSTRAINT DF_AttrDef_IsActive DEFAULT 1,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_AttrDef_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_AttributeDefinition PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_AttrDef_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_AttrDef_Code UNIQUE NONCLUSTERED (TenantId, EntityType, Code),
        CONSTRAINT FK_AttrDef_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_AttrDef_Entity CHECK (EntityType IN ('Product','Customer','Supplier','SalesOrder','PurchaseOrder')),
        CONSTRAINT CK_AttrDef_DataType CHECK (DataType IN ('Text','Number','Decimal','Date','Boolean','List','MultiList')),
        CONSTRAINT CK_AttrDef_Options_Json CHECK (OptionsJson IS NULL OR ISJSON(OptionsJson) = 1)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_AttributeDefinition ON catalog.AttributeDefinition (TenantId, ClusterKey);
    CREATE INDEX IX_AttributeDefinition_Sync ON catalog.AttributeDefinition (TenantId, RowVer);

    CREATE TABLE catalog.Category
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Category_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        ParentId        uniqueidentifier NULL,
        Code            nvarchar(40)     NULL,
        Name            nvarchar(150)    NOT NULL,
        SortOrder       int              NOT NULL CONSTRAINT DF_Category_Sort DEFAULT 0,
        IsActive        bit              NOT NULL CONSTRAINT DF_Category_IsActive DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Category_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_Category PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Category_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT FK_Category_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT FK_Category_Parent FOREIGN KEY (TenantId, ParentId) REFERENCES catalog.Category (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Category ON catalog.Category (TenantId, ClusterKey);
    CREATE INDEX IX_Category_Sync ON catalog.Category (TenantId, RowVer);

    -- Which custom attributes a category shows on its products
    CREATE TABLE catalog.CategoryAttribute
    (
        TenantId                uniqueidentifier NOT NULL,
        CategoryId              uniqueidentifier NOT NULL,
        AttributeDefinitionId   uniqueidentifier NOT NULL,
        SortOrder               int              NOT NULL CONSTRAINT DF_CategoryAttribute_Sort DEFAULT 0,
        CONSTRAINT PK_CategoryAttribute PRIMARY KEY CLUSTERED (TenantId, CategoryId, AttributeDefinitionId),
        CONSTRAINT FK_CategoryAttribute_Category FOREIGN KEY (TenantId, CategoryId) REFERENCES catalog.Category (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_CategoryAttribute_AttrDef FOREIGN KEY (TenantId, AttributeDefinitionId) REFERENCES catalog.AttributeDefinition (TenantId, Id)
    );

    CREATE TABLE catalog.Brand
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Brand_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        Name            nvarchar(150)    NOT NULL,
        Manufacturer    nvarchar(200)    NULL,
        IsActive        bit              NOT NULL CONSTRAINT DF_Brand_IsActive DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Brand_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_Brand PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Brand_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Brand_Name UNIQUE NONCLUSTERED (TenantId, Name),
        CONSTRAINT FK_Brand_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Brand ON catalog.Brand (TenantId, ClusterKey);

    -- Tax codes exist from day one with RatePercent = 0, so tax can be switched on later
    -- without migrating posted documents.
    CREATE TABLE catalog.TaxCode
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_TaxCode_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        Code            nvarchar(20)     NOT NULL,   -- 'NONE', 'GST', 'PST'
        Name            nvarchar(100)    NOT NULL,
        RatePercent     decimal(9,4)     NOT NULL CONSTRAINT DF_TaxCode_Rate DEFAULT 0,
        IsActive        bit              NOT NULL CONSTRAINT DF_TaxCode_IsActive DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_TaxCode_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_TaxCode PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_TaxCode_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_TaxCode_Code UNIQUE NONCLUSTERED (TenantId, Code),
        CONSTRAINT FK_TaxCode_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_TaxCode_Rate CHECK (RatePercent >= 0 AND RatePercent <= 100)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_TaxCode ON catalog.TaxCode (TenantId, ClusterKey);
    CREATE INDEX IX_TaxCode_Sync ON catalog.TaxCode (TenantId, RowVer);

    CREATE TABLE catalog.Product
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_Product_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        Sku                 nvarchar(64)     NOT NULL,
        Name                nvarchar(200)    NOT NULL,
        ShortName           nvarchar(60)     NULL,       -- receipts and kitchen tickets
        Description         nvarchar(2000)   NULL,
        ProductTypeId       uniqueidentifier NOT NULL,
        CategoryId          uniqueidentifier NULL,
        BrandId             uniqueidentifier NULL,
        BaseUomId           uniqueidentifier NOT NULL,  -- the ledger always stores base units
        PurchaseUomId       uniqueidentifier NULL,
        SalesUomId          uniqueidentifier NULL,
        TaxCodeId           uniqueidentifier NULL,
        TrackingMode        varchar(20)      NOT NULL CONSTRAINT DF_Product_Tracking DEFAULT 'None',
        IssueRule           varchar(10)      NOT NULL CONSTRAINT DF_Product_Issue DEFAULT 'FIFO',
        AllowNegativeStock  bit              NOT NULL CONSTRAINT DF_Product_Negative DEFAULT 0,
        IsSellable          bit              NOT NULL CONSTRAINT DF_Product_Sellable DEFAULT 1,
        IsPurchasable       bit              NOT NULL CONSTRAINT DF_Product_Purchasable DEFAULT 1,
        MinShelfLifeDays    int              NULL,       -- reject receipts expiring sooner (pharma)
        ParentProductId     uniqueidentifier NULL,       -- set on variants; parent holds shared data
        VariantValuesJson   nvarchar(max)    NULL,       -- {"size":"120 ml"}
        AttributesJson      nvarchar(max)    NULL,       -- custom attribute values, validated by the API
        ImageAttachmentId   uniqueidentifier NULL,
        IsActive            bit              NOT NULL CONSTRAINT DF_Product_IsActive DEFAULT 1,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_Product_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_Product PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Product_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Product_Sku UNIQUE NONCLUSTERED (TenantId, Sku),
        CONSTRAINT FK_Product_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT FK_Product_Type FOREIGN KEY (TenantId, ProductTypeId) REFERENCES catalog.ProductType (TenantId, Id),
        CONSTRAINT FK_Product_Category FOREIGN KEY (TenantId, CategoryId) REFERENCES catalog.Category (TenantId, Id),
        CONSTRAINT FK_Product_Brand FOREIGN KEY (TenantId, BrandId) REFERENCES catalog.Brand (TenantId, Id),
        CONSTRAINT FK_Product_BaseUom FOREIGN KEY (TenantId, BaseUomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT FK_Product_PurchaseUom FOREIGN KEY (TenantId, PurchaseUomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT FK_Product_SalesUom FOREIGN KEY (TenantId, SalesUomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT FK_Product_TaxCode FOREIGN KEY (TenantId, TaxCodeId) REFERENCES catalog.TaxCode (TenantId, Id),
        CONSTRAINT FK_Product_Parent FOREIGN KEY (TenantId, ParentProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_Product_Image FOREIGN KEY (TenantId, ImageAttachmentId) REFERENCES platform.Attachment (TenantId, Id),
        CONSTRAINT CK_Product_Tracking CHECK (TrackingMode IN ('None','Batch','BatchExpiry','Serial')),
        CONSTRAINT CK_Product_Issue CHECK (IssueRule IN ('FIFO','FEFO','Manual')),
        CONSTRAINT CK_Product_Variant_Json CHECK (VariantValuesJson IS NULL OR ISJSON(VariantValuesJson) = 1),
        CONSTRAINT CK_Product_Attributes_Json CHECK (AttributesJson IS NULL OR ISJSON(AttributesJson) = 1),
        CONSTRAINT CK_Product_ShelfLife CHECK (MinShelfLifeDays IS NULL OR MinShelfLifeDays >= 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Product ON catalog.Product (TenantId, ClusterKey);
    CREATE INDEX IX_Product_Name ON catalog.Product (TenantId, Name) INCLUDE (Sku, IsActive);
    CREATE INDEX IX_Product_Category ON catalog.Product (TenantId, CategoryId);
    CREATE INDEX IX_Product_Sync ON catalog.Product (TenantId, RowVer);
    -- Searchable custom attributes: later scripts add persisted computed columns, e.g.
    -- ALTER TABLE catalog.Product ADD Attr_GenericName AS CAST(JSON_VALUE(AttributesJson,'$.genericName') AS nvarchar(200)) PERSISTED;
    -- CREATE INDEX IX_Product_Attr_GenericName ON catalog.Product (TenantId, Attr_GenericName);

    -- Unit conversions per product: 1 <Uom> = FactorToBase base units
    CREATE TABLE catalog.ProductUomConversion
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_ProdUom_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        ProductId       uniqueidentifier NOT NULL,
        UomId           uniqueidentifier NOT NULL,
        FactorToBase    decimal(18,6)    NOT NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_ProductUomConversion PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_ProductUomConversion UNIQUE NONCLUSTERED (TenantId, ProductId, UomId),
        CONSTRAINT FK_ProdUom_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_ProdUom_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT CK_ProdUom_Factor CHECK (FactorToBase > 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_ProductUomConversion ON catalog.ProductUomConversion (TenantId, ClusterKey);
    CREATE INDEX IX_ProductUomConversion_Sync ON catalog.ProductUomConversion (TenantId, RowVer);

    CREATE TABLE catalog.Barcode
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Barcode_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        ProductId       uniqueidentifier NOT NULL,
        UomId           uniqueidentifier NULL,       -- barcode on a box vs a strip
        Code            nvarchar(64)     NOT NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_Barcode PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_Barcode_Code UNIQUE NONCLUSTERED (TenantId, Code),
        CONSTRAINT FK_Barcode_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_Barcode_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Barcode ON catalog.Barcode (TenantId, ClusterKey);
    CREATE INDEX IX_Barcode_Sync ON catalog.Barcode (TenantId, RowVer);

    -- Bill of materials for Kit and Recipe products (consumed when the parent is sold or produced)
    CREATE TABLE catalog.ProductComponent
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_ProdComp_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        ParentProductId     uniqueidentifier NOT NULL,
        ComponentProductId  uniqueidentifier NOT NULL,
        Quantity            decimal(18,6)    NOT NULL,
        UomId               uniqueidentifier NOT NULL,
        WastagePercent      decimal(9,4)     NOT NULL CONSTRAINT DF_ProdComp_Wastage DEFAULT 0,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_ProductComponent PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_ProductComponent UNIQUE NONCLUSTERED (TenantId, ParentProductId, ComponentProductId),
        CONSTRAINT FK_ProdComp_Parent FOREIGN KEY (TenantId, ParentProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_ProdComp_Component FOREIGN KEY (TenantId, ComponentProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_ProdComp_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT CK_ProdComp_Qty CHECK (Quantity > 0),
        CONSTRAINT CK_ProdComp_NotSelf CHECK (ParentProductId <> ComponentProductId)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_ProductComponent ON catalog.ProductComponent (TenantId, ClusterKey);

    CREATE TABLE catalog.PriceList
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_PriceList_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        Name            nvarchar(100)    NOT NULL,
        Currency        char(3)          NOT NULL CONSTRAINT DF_PriceList_Currency DEFAULT 'PKR',
        IsDefault       bit              NOT NULL CONSTRAINT DF_PriceList_IsDefault DEFAULT 0,
        ValidFrom       date             NULL,
        ValidTo         date             NULL,
        IsActive        bit              NOT NULL CONSTRAINT DF_PriceList_IsActive DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_PriceList_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_PriceList PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_PriceList_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_PriceList_Name UNIQUE NONCLUSTERED (TenantId, Name),
        CONSTRAINT FK_PriceList_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_PriceList_Dates CHECK (ValidTo IS NULL OR ValidFrom IS NULL OR ValidTo >= ValidFrom)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_PriceList ON catalog.PriceList (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_PriceList_OneDefault ON catalog.PriceList (TenantId) WHERE IsDefault = 1;

    CREATE TABLE catalog.PriceListItem
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_PriceListItem_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        PriceListId     uniqueidentifier NOT NULL,
        ProductId       uniqueidentifier NOT NULL,
        UomId           uniqueidentifier NOT NULL,
        MinQuantity     decimal(18,6)    NOT NULL CONSTRAINT DF_PriceListItem_MinQty DEFAULT 1, -- quantity tiers
        UnitPrice       decimal(19,4)    NOT NULL,
        ValidFrom       date             NULL,
        ValidTo         date             NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_PriceListItem PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_PriceListItem UNIQUE NONCLUSTERED (TenantId, PriceListId, ProductId, UomId, MinQuantity, ValidFrom),
        CONSTRAINT FK_PriceListItem_PriceList FOREIGN KEY (TenantId, PriceListId) REFERENCES catalog.PriceList (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_PriceListItem_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_PriceListItem_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT CK_PriceListItem_Price CHECK (UnitPrice >= 0),
        CONSTRAINT CK_PriceListItem_MinQty CHECK (MinQuantity > 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_PriceListItem ON catalog.PriceListItem (TenantId, ClusterKey);
    CREATE INDEX IX_PriceListItem_Product ON catalog.PriceListItem (TenantId, ProductId, PriceListId);
    CREATE INDEX IX_PriceListItem_Sync ON catalog.PriceListItem (TenantId, RowVer);

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'003_Catalog');
    COMMIT TRANSACTION;
    PRINT N'003_Catalog applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
