/*
  004_Inventory.sql
  Warehouses, locations, batches, the append-only stock ledger, stock balances,
  moving-average cost, reservations, reorder rules, reason codes, configurable stock
  document types, stock documents (opening stock, adjustments, transfers, wastage...)
  and stock counts.

  Rules enforced here:
  - inventory.StockLedgerEntry is append-only (trigger). Corrections are reversing entries.
  - Every balance row has a BatchId. Products without batch tracking use one default batch.
  - QtyReserved can never be negative; QtyOnHand may go negative only if the product allows it
    (checked by the API's atomic UPDATE, not by a constraint, because offline sales can oversell).
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'004_Inventory')
BEGIN
    PRINT N'004_Inventory already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE inventory.Warehouse
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Warehouse_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        BranchId        uniqueidentifier NOT NULL,
        Code            nvarchar(32)     NOT NULL,
        Name            nvarchar(150)    NOT NULL,
        IsActive        bit              NOT NULL CONSTRAINT DF_Warehouse_IsActive DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Warehouse_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_Warehouse PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Warehouse_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Warehouse_Code UNIQUE NONCLUSTERED (TenantId, Code),
        CONSTRAINT FK_Warehouse_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Warehouse ON inventory.Warehouse (TenantId, ClusterKey);
    CREATE INDEX IX_Warehouse_Sync ON inventory.Warehouse (TenantId, RowVer);

    -- Every warehouse gets at least a 'MAIN' Storage location and an 'TRANSIT' InTransit location
    CREATE TABLE inventory.Location
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Location_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        WarehouseId     uniqueidentifier NOT NULL,
        Code            nvarchar(32)     NOT NULL,
        Name            nvarchar(150)    NOT NULL,
        LocationType    varchar(20)      NOT NULL CONSTRAINT DF_Location_Type DEFAULT 'Storage',
        IsSellable      bit              NOT NULL CONSTRAINT DF_Location_Sellable DEFAULT 1, -- stock here can be reserved for orders
        IsActive        bit              NOT NULL CONSTRAINT DF_Location_IsActive DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Location_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_Location PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Location_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Location_Code UNIQUE NONCLUSTERED (TenantId, WarehouseId, Code),
        CONSTRAINT FK_Location_Warehouse FOREIGN KEY (TenantId, WarehouseId) REFERENCES inventory.Warehouse (TenantId, Id),
        CONSTRAINT CK_Location_Type CHECK (LocationType IN ('Storage','Quarantine','InTransit','ColdStore','Damaged','Kitchen'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Location ON inventory.Location (TenantId, ClusterKey);
    CREATE INDEX IX_Location_Sync ON inventory.Location (TenantId, RowVer);

    CREATE TABLE inventory.Batch
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Batch_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        ProductId       uniqueidentifier NOT NULL,
        BatchNo         nvarchar(64)     NOT NULL,   -- default batch uses 'DEFAULT'
        MfgDate         date             NULL,
        ExpiryDate      date             NULL,
        IsDefault       bit              NOT NULL CONSTRAINT DF_Batch_IsDefault DEFAULT 0,
        Status          varchar(20)      NOT NULL CONSTRAINT DF_Batch_Status DEFAULT 'Active',
        StatusReason    nvarchar(500)    NULL,
        StatusChangedAtUtc datetime2(3)  NULL,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Batch_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_Batch PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Batch_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Batch_No UNIQUE NONCLUSTERED (TenantId, ProductId, BatchNo),
        CONSTRAINT FK_Batch_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT CK_Batch_Status CHECK (Status IN ('Active','Blocked','Recalled','Expired')),
        CONSTRAINT CK_Batch_Dates CHECK (ExpiryDate IS NULL OR MfgDate IS NULL OR ExpiryDate >= MfgDate)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Batch ON inventory.Batch (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_Batch_OneDefault ON inventory.Batch (TenantId, ProductId) WHERE IsDefault = 1;
    CREATE INDEX IX_Batch_Expiry ON inventory.Batch (TenantId, ExpiryDate) INCLUDE (ProductId, Status) WHERE ExpiryDate IS NOT NULL;
    CREATE INDEX IX_Batch_Sync ON inventory.Batch (TenantId, RowVer);

    -- Source of truth for stock. Quantities in BASE units. Never updated or deleted.
    CREATE TABLE inventory.StockLedgerEntry
    (
        Id                  bigint IDENTITY(1,1) NOT NULL,
        TenantId            uniqueidentifier NOT NULL,
        ProductId           uniqueidentifier NOT NULL,
        LocationId          uniqueidentifier NOT NULL,
        BatchId             uniqueidentifier NOT NULL,
        MovementKind        varchar(20)      NOT NULL,
        QtyChange           decimal(18,6)    NOT NULL,   -- signed: + in, - out
        UnitCost            decimal(19,6)    NOT NULL,   -- cost per base unit at posting
        DocumentType        varchar(40)      NOT NULL,   -- 'GoodsReceipt', 'StockDocument', 'SalesOrder', 'SalesReturn'...
        DocumentId          uniqueidentifier NOT NULL,
        DocumentLineId      uniqueidentifier NULL,
        DocumentNo          nvarchar(40)     NULL,
        BusinessDate        date             NOT NULL,
        PostedAtUtc         datetime2(3)     NOT NULL CONSTRAINT DF_Ledger_PostedAtUtc DEFAULT SYSUTCDATETIME(),
        PostedBy            uniqueidentifier NULL,
        ReversalOfEntryId   bigint           NULL,
        CONSTRAINT PK_StockLedgerEntry PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_Ledger_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_Ledger_Location FOREIGN KEY (TenantId, LocationId) REFERENCES inventory.Location (TenantId, Id),
        CONSTRAINT FK_Ledger_Batch FOREIGN KEY (TenantId, BatchId) REFERENCES inventory.Batch (TenantId, Id),
        CONSTRAINT FK_Ledger_Reversal FOREIGN KEY (ReversalOfEntryId) REFERENCES inventory.StockLedgerEntry (Id),
        CONSTRAINT CK_Ledger_Kind CHECK (MovementKind IN ('Receipt','Issue','Transfer','Adjustment','Count','Consumption')),
        CONSTRAINT CK_Ledger_Qty CHECK (QtyChange <> 0),
        CONSTRAINT CK_Ledger_Cost CHECK (UnitCost >= 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_StockLedgerEntry ON inventory.StockLedgerEntry (TenantId, Id);
    CREATE INDEX IX_Ledger_Product ON inventory.StockLedgerEntry (TenantId, ProductId, BusinessDate) INCLUDE (LocationId, BatchId, QtyChange, UnitCost);
    CREATE INDEX IX_Ledger_Document ON inventory.StockLedgerEntry (TenantId, DocumentType, DocumentId);

    -- Projection of the ledger; rebuildable at any time. One row per product/location/batch.
    CREATE TABLE inventory.StockBalance
    (
        TenantId        uniqueidentifier NOT NULL,
        ProductId       uniqueidentifier NOT NULL,
        LocationId      uniqueidentifier NOT NULL,
        BatchId         uniqueidentifier NOT NULL,
        QtyOnHand       decimal(18,6)    NOT NULL CONSTRAINT DF_Balance_OnHand DEFAULT 0,
        QtyReserved     decimal(18,6)    NOT NULL CONSTRAINT DF_Balance_Reserved DEFAULT 0,
        QtyAvailable    AS (QtyOnHand - QtyReserved) PERSISTED,
        LastMovementAtUtc datetime2(3)   NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_StockBalance PRIMARY KEY CLUSTERED (TenantId, ProductId, LocationId, BatchId),
        CONSTRAINT FK_Balance_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_Balance_Location FOREIGN KEY (TenantId, LocationId) REFERENCES inventory.Location (TenantId, Id),
        CONSTRAINT FK_Balance_Batch FOREIGN KEY (TenantId, BatchId) REFERENCES inventory.Batch (TenantId, Id),
        CONSTRAINT CK_Balance_Reserved CHECK (QtyReserved >= 0)
    );
    CREATE INDEX IX_StockBalance_Location ON inventory.StockBalance (TenantId, LocationId) INCLUDE (ProductId, BatchId, QtyOnHand, QtyReserved);
    CREATE INDEX IX_StockBalance_Sync ON inventory.StockBalance (TenantId, RowVer);

    -- Moving weighted-average cost per product per warehouse
    CREATE TABLE inventory.ProductCost
    (
        TenantId        uniqueidentifier NOT NULL,
        ProductId       uniqueidentifier NOT NULL,
        WarehouseId     uniqueidentifier NOT NULL,
        AvgUnitCost     decimal(19,6)    NOT NULL CONSTRAINT DF_ProductCost_Avg DEFAULT 0,
        QtyBasis        decimal(18,6)    NOT NULL CONSTRAINT DF_ProductCost_Qty DEFAULT 0,
        LastCost        decimal(19,6)    NULL,
        UpdatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_ProductCost_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_ProductCost PRIMARY KEY CLUSTERED (TenantId, ProductId, WarehouseId),
        CONSTRAINT FK_ProductCost_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_ProductCost_Warehouse FOREIGN KEY (TenantId, WarehouseId) REFERENCES inventory.Warehouse (TenantId, Id)
    );

    CREATE TABLE inventory.Reservation
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_Reservation_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        ProductId           uniqueidentifier NOT NULL,
        LocationId          uniqueidentifier NOT NULL,
        BatchId             uniqueidentifier NOT NULL,
        Quantity            decimal(18,6)    NOT NULL,   -- base units
        SourceDocumentType  varchar(40)      NOT NULL,
        SourceDocumentId    uniqueidentifier NOT NULL,
        SourceLineId        uniqueidentifier NULL,
        Status              varchar(20)      NOT NULL CONSTRAINT DF_Reservation_Status DEFAULT 'Active',
        ExpiresAtUtc        datetime2(3)     NULL,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_Reservation_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        ClosedAtUtc         datetime2(3)     NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_Reservation PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_Reservation_Balance FOREIGN KEY (TenantId, ProductId, LocationId, BatchId)
            REFERENCES inventory.StockBalance (TenantId, ProductId, LocationId, BatchId),
        CONSTRAINT CK_Reservation_Qty CHECK (Quantity > 0),
        CONSTRAINT CK_Reservation_Status CHECK (Status IN ('Active','Consumed','Released','Expired'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Reservation ON inventory.Reservation (TenantId, ClusterKey);
    CREATE INDEX IX_Reservation_Source ON inventory.Reservation (TenantId, SourceDocumentType, SourceDocumentId);
    CREATE INDEX IX_Reservation_Expiry ON inventory.Reservation (ExpiresAtUtc) WHERE Status = 'Active' AND ExpiresAtUtc IS NOT NULL;

    CREATE TABLE inventory.ReorderRule
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_ReorderRule_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        ProductId           uniqueidentifier NOT NULL,
        WarehouseId         uniqueidentifier NOT NULL,
        ReorderLevel        decimal(18,6)    NOT NULL,   -- base units
        ReorderQuantity     decimal(18,6)    NOT NULL,
        PreferredSupplierId uniqueidentifier NULL,       -- FK added in 005 (purchasing.Supplier)
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_ReorderRule PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_ReorderRule UNIQUE NONCLUSTERED (TenantId, ProductId, WarehouseId),
        CONSTRAINT FK_ReorderRule_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_ReorderRule_Warehouse FOREIGN KEY (TenantId, WarehouseId) REFERENCES inventory.Warehouse (TenantId, Id),
        CONSTRAINT CK_ReorderRule_Qty CHECK (ReorderLevel >= 0 AND ReorderQuantity > 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_ReorderRule ON inventory.ReorderRule (TenantId, ClusterKey);

    CREATE TABLE inventory.ReasonCode
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_ReasonCode_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        Code            nvarchar(32)     NOT NULL,   -- 'DAMAGE', 'EXPIRED', 'THEFT', 'WASTAGE', 'STAFF_MEAL'
        Name            nvarchar(100)    NOT NULL,
        AppliesTo       varchar(30)      NOT NULL CONSTRAINT DF_ReasonCode_AppliesTo DEFAULT 'Adjustment',
        IsActive        bit              NOT NULL CONSTRAINT DF_ReasonCode_IsActive DEFAULT 1,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_ReasonCode PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_ReasonCode_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_ReasonCode_Code UNIQUE NONCLUSTERED (TenantId, Code),
        CONSTRAINT FK_ReasonCode_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_ReasonCode_AppliesTo CHECK (AppliesTo IN ('Adjustment','Return','Void','Cancellation','Discount'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_ReasonCode ON inventory.ReasonCode (TenantId, ClusterKey);
    CREATE INDEX IX_ReasonCode_Sync ON inventory.ReasonCode (TenantId, RowVer);

    -- Configurable stock document types, each mapped to one fixed movement kind
    CREATE TABLE inventory.StockDocumentType
    (
        TenantId                uniqueidentifier NOT NULL,
        Id                      uniqueidentifier NOT NULL CONSTRAINT DF_StockDocType_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey              bigint IDENTITY(1,1) NOT NULL,
        Code                    varchar(40)      NOT NULL,   -- 'OPENING', 'ADJUST', 'TRANSFER', 'WASTAGE', 'STAFF_MEAL'
        Name                    nvarchar(100)    NOT NULL,
        MovementKind            varchar(20)      NOT NULL,
        Direction               varchar(10)      NOT NULL,   -- In, Out, Both (adjustment +/-), Move (transfer)
        RequiresReason          bit              NOT NULL CONSTRAINT DF_StockDocType_Reason DEFAULT 0,
        RequiresApproval        bit              NOT NULL CONSTRAINT DF_StockDocType_Approval DEFAULT 0,
        ApprovalThresholdValue  decimal(19,4)    NULL,       -- approval only above this cost value
        NumberSeriesType        varchar(40)      NOT NULL,   -- platform.NumberSeries.DocumentType
        IsSystem                bit              NOT NULL CONSTRAINT DF_StockDocType_IsSystem DEFAULT 0,
        IsActive                bit              NOT NULL CONSTRAINT DF_StockDocType_IsActive DEFAULT 1,
        RowVer                  rowversion       NOT NULL,
        CONSTRAINT PK_StockDocumentType PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_StockDocType_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_StockDocType_Code UNIQUE NONCLUSTERED (TenantId, Code),
        CONSTRAINT FK_StockDocType_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_StockDocType_Kind CHECK (MovementKind IN ('Receipt','Issue','Transfer','Adjustment','Count','Consumption')),
        CONSTRAINT CK_StockDocType_Direction CHECK (Direction IN ('In','Out','Both','Move'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_StockDocumentType ON inventory.StockDocumentType (TenantId, ClusterKey);

    CREATE TABLE inventory.StockDocument
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_StockDoc_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        DocumentTypeId      uniqueidentifier NOT NULL,
        DocumentNo          nvarchar(40)     NULL,       -- assigned when posted
        Status              varchar(20)      NOT NULL CONSTRAINT DF_StockDoc_Status DEFAULT 'Draft',
        WarehouseId         uniqueidentifier NOT NULL,
        ToWarehouseId       uniqueidentifier NULL,       -- transfers only
        BusinessDate        date             NOT NULL,
        ReasonCodeId        uniqueidentifier NULL,
        Reference           nvarchar(100)    NULL,
        Notes               nvarchar(1000)   NULL,
        TotalCostValue      decimal(19,4)    NULL,       -- used for approval thresholds
        ClientDocumentId    uniqueidentifier NULL,       -- idempotency for offline creation
        SourceDeviceId      uniqueidentifier NULL,
        ApprovedBy          uniqueidentifier NULL,
        ApprovedAtUtc       datetime2(3)     NULL,
        PostedBy            uniqueidentifier NULL,
        PostedAtUtc         datetime2(3)     NULL,
        DispatchedAtUtc     datetime2(3)     NULL,       -- transfer step 1 (out to in-transit)
        ReceivedAtUtc       datetime2(3)     NULL,       -- transfer step 2 (in at destination)
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_StockDoc_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_StockDocument PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_StockDoc_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT FK_StockDoc_Type FOREIGN KEY (TenantId, DocumentTypeId) REFERENCES inventory.StockDocumentType (TenantId, Id),
        CONSTRAINT FK_StockDoc_Warehouse FOREIGN KEY (TenantId, WarehouseId) REFERENCES inventory.Warehouse (TenantId, Id),
        CONSTRAINT FK_StockDoc_ToWarehouse FOREIGN KEY (TenantId, ToWarehouseId) REFERENCES inventory.Warehouse (TenantId, Id),
        CONSTRAINT FK_StockDoc_Reason FOREIGN KEY (TenantId, ReasonCodeId) REFERENCES inventory.ReasonCode (TenantId, Id),
        CONSTRAINT CK_StockDoc_Status CHECK (Status IN ('Draft','PendingApproval','Approved','InTransit','Posted','Cancelled','Reversed'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_StockDocument ON inventory.StockDocument (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_StockDoc_No ON inventory.StockDocument (TenantId, DocumentNo) WHERE DocumentNo IS NOT NULL;
    CREATE UNIQUE INDEX UX_StockDoc_ClientId ON inventory.StockDocument (TenantId, ClientDocumentId) WHERE ClientDocumentId IS NOT NULL;
    CREATE INDEX IX_StockDoc_Status ON inventory.StockDocument (TenantId, Status, BusinessDate);

    CREATE TABLE inventory.StockDocumentLine
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_StockDocLine_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        StockDocumentId     uniqueidentifier NOT NULL,
        LineNumber              int              NOT NULL,
        ProductId           uniqueidentifier NOT NULL,
        UomId               uniqueidentifier NOT NULL,
        Quantity            decimal(18,6)    NOT NULL,   -- in UomId; sign per document Direction ('Both' allows +/-)
        QtyBase             decimal(18,6)    NOT NULL,   -- converted to base units by the API
        BatchId             uniqueidentifier NULL,       -- existing batch
        NewBatchNo          nvarchar(64)     NULL,       -- receipt-type documents may create a batch
        NewBatchMfgDate     date             NULL,
        NewBatchExpiryDate  date             NULL,
        FromLocationId      uniqueidentifier NULL,
        ToLocationId        uniqueidentifier NULL,
        UnitCost            decimal(19,6)    NULL,       -- opening stock / positive adjustments
        Notes               nvarchar(500)    NULL,
        CONSTRAINT PK_StockDocumentLine PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_StockDocLine_LineNo UNIQUE NONCLUSTERED (TenantId, StockDocumentId, LineNumber),
        CONSTRAINT FK_StockDocLine_Doc FOREIGN KEY (TenantId, StockDocumentId) REFERENCES inventory.StockDocument (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_StockDocLine_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_StockDocLine_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT FK_StockDocLine_Batch FOREIGN KEY (TenantId, BatchId) REFERENCES inventory.Batch (TenantId, Id),
        CONSTRAINT FK_StockDocLine_FromLoc FOREIGN KEY (TenantId, FromLocationId) REFERENCES inventory.Location (TenantId, Id),
        CONSTRAINT FK_StockDocLine_ToLoc FOREIGN KEY (TenantId, ToLocationId) REFERENCES inventory.Location (TenantId, Id),
        CONSTRAINT CK_StockDocLine_Qty CHECK (Quantity <> 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_StockDocumentLine ON inventory.StockDocumentLine (TenantId, ClusterKey);

    CREATE TABLE inventory.StockCount
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_StockCount_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        DocumentNo          nvarchar(40)     NULL,
        WarehouseId         uniqueidentifier NOT NULL,
        CountType           varchar(10)      NOT NULL CONSTRAINT DF_StockCount_Type DEFAULT 'Full',
        IsBlind             bit              NOT NULL CONSTRAINT DF_StockCount_Blind DEFAULT 1, -- counters do not see system qty
        Status              varchar(20)      NOT NULL CONSTRAINT DF_StockCount_Status DEFAULT 'Open',
        SnapshotAtUtc       datetime2(3)     NULL,
        BusinessDate        date             NOT NULL,
        AdjustmentDocumentId uniqueidentifier NULL,      -- the StockDocument created when posted
        Notes               nvarchar(1000)   NULL,
        PostedBy            uniqueidentifier NULL,
        PostedAtUtc         datetime2(3)     NULL,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_StockCount_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_StockCount PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_StockCount_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT FK_StockCount_Warehouse FOREIGN KEY (TenantId, WarehouseId) REFERENCES inventory.Warehouse (TenantId, Id),
        CONSTRAINT FK_StockCount_AdjDoc FOREIGN KEY (TenantId, AdjustmentDocumentId) REFERENCES inventory.StockDocument (TenantId, Id),
        CONSTRAINT CK_StockCount_Type CHECK (CountType IN ('Full','Cycle')),
        CONSTRAINT CK_StockCount_Status CHECK (Status IN ('Open','Counting','Review','Posted','Cancelled'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_StockCount ON inventory.StockCount (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_StockCount_No ON inventory.StockCount (TenantId, DocumentNo) WHERE DocumentNo IS NOT NULL;

    CREATE TABLE inventory.StockCountLine
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_StockCountLine_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        StockCountId    uniqueidentifier NOT NULL,
        ProductId       uniqueidentifier NOT NULL,
        LocationId      uniqueidentifier NOT NULL,
        BatchId         uniqueidentifier NOT NULL,
        SystemQty       decimal(18,6)    NOT NULL,   -- base units at snapshot
        CountedQty      decimal(18,6)    NULL,       -- base units
        VarianceQty     AS (CountedQty - SystemQty),
        CountedBy       uniqueidentifier NULL,
        CountedAtUtc    datetime2(3)     NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_StockCountLine PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_StockCountLine UNIQUE NONCLUSTERED (TenantId, StockCountId, ProductId, LocationId, BatchId),
        CONSTRAINT FK_StockCountLine_Count FOREIGN KEY (TenantId, StockCountId) REFERENCES inventory.StockCount (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_StockCountLine_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_StockCountLine_Location FOREIGN KEY (TenantId, LocationId) REFERENCES inventory.Location (TenantId, Id),
        CONSTRAINT FK_StockCountLine_Batch FOREIGN KEY (TenantId, BatchId) REFERENCES inventory.Batch (TenantId, Id),
        CONSTRAINT CK_StockCountLine_Counted CHECK (CountedQty IS NULL OR CountedQty >= 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_StockCountLine ON inventory.StockCountLine (TenantId, ClusterKey);

    -- Block UPDATE/DELETE on the ledger
    EXEC(N'CREATE TRIGGER inventory.TR_StockLedgerEntry_AppendOnly ON inventory.StockLedgerEntry
          INSTEAD OF UPDATE, DELETE AS
          BEGIN
              SET NOCOUNT ON;
              THROW 50002, N''inventory.StockLedgerEntry is append-only. Post a reversing entry instead.'', 1;
          END');

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'004_Inventory');
    COMMIT TRANSACTION;
    PRINT N'004_Inventory applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
