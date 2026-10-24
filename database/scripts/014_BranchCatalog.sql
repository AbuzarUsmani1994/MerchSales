/*
  014_BranchCatalog.sql
  Branch-level menu control, agreed with the owner (not in the original Build Spec):

  - The tenant's menu is shared by all branches and edited by the Owner (all-branch roles).
  - A branch manager may, for their own branch only:
      * charge a different price  -> a price list linked to the branch (catalog.PriceList.BranchId)
      * mark an item unavailable  -> catalog.ProductBranch.IsAvailable = 0
      * add an item only their branch sells -> catalog.Product.OwnerBranchId
  - Price resolution for a sale at branch B: B's price list first, then the tenant default.

  Run after 013. Safe to run once; re-running is skipped.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'014_BranchCatalog')
BEGIN
    PRINT N'014_BranchCatalog already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    -- NULL = on the shared menu of every branch; set = sold only at that branch.
    ALTER TABLE catalog.Product ADD OwnerBranchId uniqueidentifier NULL;
    -- Statements that use a column added in this same batch must go through EXEC: SQL Server
    -- compiles the whole batch before ALTER TABLE ... ADD has run.
    EXEC(N'ALTER TABLE catalog.Product ADD CONSTRAINT FK_Product_OwnerBranch
        FOREIGN KEY (TenantId, OwnerBranchId) REFERENCES tenancy.Branch (TenantId, Id);');
    EXEC(N'CREATE INDEX IX_Product_OwnerBranch ON catalog.Product (TenantId, OwnerBranchId) WHERE OwnerBranchId IS NOT NULL;');

    -- NULL = tenant-wide list (the default menu, customer lists); set = that branch's own prices.
    ALTER TABLE catalog.PriceList ADD BranchId uniqueidentifier NULL;
    EXEC(N'ALTER TABLE catalog.PriceList ADD CONSTRAINT FK_PriceList_Branch
        FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id);');
    EXEC(N'ALTER TABLE catalog.PriceList ADD CONSTRAINT CK_PriceList_BranchNotDefault CHECK (BranchId IS NULL OR IsDefault = 0);');
    EXEC(N'CREATE UNIQUE INDEX UX_PriceList_OnePerBranch ON catalog.PriceList (TenantId, BranchId) WHERE BranchId IS NOT NULL;');
    CREATE INDEX IX_PriceList_Sync ON catalog.PriceList (TenantId, RowVer);

    -- Per-branch availability of a menu item. No row = available.
    CREATE TABLE catalog.ProductBranch
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_ProductBranch_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        ProductId       uniqueidentifier NOT NULL,
        BranchId        uniqueidentifier NOT NULL,
        IsAvailable     bit              NOT NULL CONSTRAINT DF_ProductBranch_IsAvailable DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_ProductBranch_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_ProductBranch PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_ProductBranch_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_ProductBranch UNIQUE NONCLUSTERED (TenantId, ProductId, BranchId),
        CONSTRAINT FK_ProductBranch_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_ProductBranch_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_ProductBranch ON catalog.ProductBranch (TenantId, ClusterKey);
    CREATE INDEX IX_ProductBranch_Sync ON catalog.ProductBranch (TenantId, RowVer);

    -- Tenant isolation for the new table (template at the end of 009_RowLevelSecurity.sql).
    EXEC(N'ALTER SECURITY POLICY security.TenantIsolation
        ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON catalog.ProductBranch,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON catalog.ProductBranch AFTER INSERT,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON catalog.ProductBranch AFTER UPDATE;');

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'014_BranchCatalog');
    COMMIT TRANSACTION;
    PRINT N'014_BranchCatalog applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
