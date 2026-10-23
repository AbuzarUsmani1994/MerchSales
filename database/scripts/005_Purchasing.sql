/*
  005_Purchasing.sql
  Suppliers, purchase orders, goods receipts (GRN = stock entry against a supplier),
  purchase returns. Supplier invoices / three-way match are out of scope for v1; the
  supplier's invoice number is recorded on the goods receipt.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'005_Purchasing')
BEGIN
    PRINT N'005_Purchasing already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE purchasing.Supplier
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_Supplier_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        Code                nvarchar(32)     NOT NULL,
        Name                nvarchar(200)    NOT NULL,
        ContactPerson       nvarchar(150)    NULL,
        Phone               nvarchar(40)     NULL,
        Email               nvarchar(256)    NULL,
        Address             nvarchar(500)    NULL,
        TaxNumber           nvarchar(40)     NULL,       -- NTN / STRN
        PaymentTermsDays    int              NOT NULL CONSTRAINT DF_Supplier_Terms DEFAULT 0,
        CustomFieldsJson    nvarchar(max)    NULL,
        IsActive            bit              NOT NULL CONSTRAINT DF_Supplier_IsActive DEFAULT 1,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_Supplier_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_Supplier PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Supplier_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Supplier_Code UNIQUE NONCLUSTERED (TenantId, Code),
        CONSTRAINT FK_Supplier_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_Supplier_Custom_Json CHECK (CustomFieldsJson IS NULL OR ISJSON(CustomFieldsJson) = 1),
        CONSTRAINT CK_Supplier_Terms CHECK (PaymentTermsDays >= 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Supplier ON purchasing.Supplier (TenantId, ClusterKey);
    CREATE INDEX IX_Supplier_Name ON purchasing.Supplier (TenantId, Name);
    CREATE INDEX IX_Supplier_Sync ON purchasing.Supplier (TenantId, RowVer);

    ALTER TABLE inventory.ReorderRule ADD CONSTRAINT FK_ReorderRule_Supplier
        FOREIGN KEY (TenantId, PreferredSupplierId) REFERENCES purchasing.Supplier (TenantId, Id);

    CREATE TABLE purchasing.PurchaseOrder
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_PO_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        DocumentNo          nvarchar(40)     NULL,       -- assigned on submit
        SupplierId          uniqueidentifier NOT NULL,
        BranchId            uniqueidentifier NOT NULL,
        WarehouseId         uniqueidentifier NOT NULL,  -- deliver to
        OrderDate           date             NOT NULL,
        ExpectedDate        date             NULL,
        Status              varchar(20)      NOT NULL CONSTRAINT DF_PO_Status DEFAULT 'Draft',
        Currency            char(3)          NOT NULL CONSTRAINT DF_PO_Currency DEFAULT 'PKR',
        SubTotal            decimal(19,4)    NOT NULL CONSTRAINT DF_PO_SubTotal DEFAULT 0,
        DiscountTotal       decimal(19,4)    NOT NULL CONSTRAINT DF_PO_DiscountTotal DEFAULT 0,
        TaxTotal            decimal(19,4)    NOT NULL CONSTRAINT DF_PO_TaxTotal DEFAULT 0,
        GrandTotal          decimal(19,4)    NOT NULL CONSTRAINT DF_PO_GrandTotal DEFAULT 0,
        Notes               nvarchar(1000)   NULL,
        CustomFieldsJson    nvarchar(max)    NULL,
        ClientDocumentId    uniqueidentifier NULL,
        SourceDeviceId      uniqueidentifier NULL,
        ApprovedBy          uniqueidentifier NULL,
        ApprovedAtUtc       datetime2(3)     NULL,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_PO_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_PurchaseOrder PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_PO_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT FK_PO_Supplier FOREIGN KEY (TenantId, SupplierId) REFERENCES purchasing.Supplier (TenantId, Id),
        CONSTRAINT FK_PO_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id),
        CONSTRAINT FK_PO_Warehouse FOREIGN KEY (TenantId, WarehouseId) REFERENCES inventory.Warehouse (TenantId, Id),
        CONSTRAINT CK_PO_Status CHECK (Status IN ('Draft','PendingApproval','Approved','Sent','PartiallyReceived','Received','Closed','Cancelled')),
        CONSTRAINT CK_PO_Custom_Json CHECK (CustomFieldsJson IS NULL OR ISJSON(CustomFieldsJson) = 1)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_PurchaseOrder ON purchasing.PurchaseOrder (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_PO_No ON purchasing.PurchaseOrder (TenantId, DocumentNo) WHERE DocumentNo IS NOT NULL;
    CREATE UNIQUE INDEX UX_PO_ClientId ON purchasing.PurchaseOrder (TenantId, ClientDocumentId) WHERE ClientDocumentId IS NOT NULL;
    CREATE INDEX IX_PO_Supplier ON purchasing.PurchaseOrder (TenantId, SupplierId, OrderDate);
    CREATE INDEX IX_PO_Status ON purchasing.PurchaseOrder (TenantId, Status);

    CREATE TABLE purchasing.PurchaseOrderLine
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_POLine_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        PurchaseOrderId     uniqueidentifier NOT NULL,
        LineNumber              int              NOT NULL,
        ProductId           uniqueidentifier NOT NULL,
        UomId               uniqueidentifier NOT NULL,
        Quantity            decimal(18,6)    NOT NULL,
        QtyBase             decimal(18,6)    NOT NULL,
        ReceivedQtyBase     decimal(18,6)    NOT NULL CONSTRAINT DF_POLine_Received DEFAULT 0,
        UnitPrice           decimal(19,4)    NOT NULL,   -- per UomId
        DiscountPercent     decimal(9,4)     NOT NULL CONSTRAINT DF_POLine_Disc DEFAULT 0,
        TaxCodeId           uniqueidentifier NULL,
        TaxRatePercent      decimal(9,4)     NOT NULL CONSTRAINT DF_POLine_TaxRate DEFAULT 0, -- snapshot
        TaxAmount           decimal(19,4)    NOT NULL CONSTRAINT DF_POLine_TaxAmt DEFAULT 0,
        LineTotal           decimal(19,4)    NOT NULL,
        Notes               nvarchar(500)    NULL,
        CONSTRAINT PK_PurchaseOrderLine PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_POLine_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_POLine_LineNo UNIQUE NONCLUSTERED (TenantId, PurchaseOrderId, LineNumber),
        CONSTRAINT FK_POLine_PO FOREIGN KEY (TenantId, PurchaseOrderId) REFERENCES purchasing.PurchaseOrder (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_POLine_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_POLine_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT FK_POLine_TaxCode FOREIGN KEY (TenantId, TaxCodeId) REFERENCES catalog.TaxCode (TenantId, Id),
        CONSTRAINT CK_POLine_Qty CHECK (Quantity > 0 AND QtyBase > 0 AND ReceivedQtyBase >= 0),
        CONSTRAINT CK_POLine_Price CHECK (UnitPrice >= 0 AND DiscountPercent BETWEEN 0 AND 100)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_PurchaseOrderLine ON purchasing.PurchaseOrderLine (TenantId, ClusterKey);

    CREATE TABLE purchasing.GoodsReceipt
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_GRN_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        DocumentNo          nvarchar(40)     NULL,       -- assigned when posted
        SupplierId          uniqueidentifier NOT NULL,
        PurchaseOrderId     uniqueidentifier NULL,       -- NULL = direct receipt without PO
        WarehouseId         uniqueidentifier NOT NULL,
        BusinessDate        date             NOT NULL,
        SupplierInvoiceNo   nvarchar(60)     NULL,
        SupplierInvoiceDate date             NULL,
        Status              varchar(20)      NOT NULL CONSTRAINT DF_GRN_Status DEFAULT 'Draft',
        Currency            char(3)          NOT NULL CONSTRAINT DF_GRN_Currency DEFAULT 'PKR',
        SubTotal            decimal(19,4)    NOT NULL CONSTRAINT DF_GRN_SubTotal DEFAULT 0,
        DiscountTotal       decimal(19,4)    NOT NULL CONSTRAINT DF_GRN_DiscountTotal DEFAULT 0,
        TaxTotal            decimal(19,4)    NOT NULL CONSTRAINT DF_GRN_TaxTotal DEFAULT 0,
        GrandTotal          decimal(19,4)    NOT NULL CONSTRAINT DF_GRN_GrandTotal DEFAULT 0,
        Notes               nvarchar(1000)   NULL,
        ClientDocumentId    uniqueidentifier NULL,
        SourceDeviceId      uniqueidentifier NULL,
        PostedBy            uniqueidentifier NULL,
        PostedAtUtc         datetime2(3)     NULL,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_GRN_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_GoodsReceipt PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_GRN_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT FK_GRN_Supplier FOREIGN KEY (TenantId, SupplierId) REFERENCES purchasing.Supplier (TenantId, Id),
        CONSTRAINT FK_GRN_PO FOREIGN KEY (TenantId, PurchaseOrderId) REFERENCES purchasing.PurchaseOrder (TenantId, Id),
        CONSTRAINT FK_GRN_Warehouse FOREIGN KEY (TenantId, WarehouseId) REFERENCES inventory.Warehouse (TenantId, Id),
        CONSTRAINT CK_GRN_Status CHECK (Status IN ('Draft','Posted','Cancelled','Reversed'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_GoodsReceipt ON purchasing.GoodsReceipt (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_GRN_No ON purchasing.GoodsReceipt (TenantId, DocumentNo) WHERE DocumentNo IS NOT NULL;
    CREATE UNIQUE INDEX UX_GRN_ClientId ON purchasing.GoodsReceipt (TenantId, ClientDocumentId) WHERE ClientDocumentId IS NOT NULL;
    CREATE INDEX IX_GRN_Supplier ON purchasing.GoodsReceipt (TenantId, SupplierId, BusinessDate);

    CREATE TABLE purchasing.GoodsReceiptLine
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_GRNLine_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        GoodsReceiptId      uniqueidentifier NOT NULL,
        LineNumber              int              NOT NULL,
        PurchaseOrderLineId uniqueidentifier NULL,
        ProductId           uniqueidentifier NOT NULL,
        UomId               uniqueidentifier NOT NULL,
        Quantity            decimal(18,6)    NOT NULL,   -- delivered, in UomId
        QtyBase             decimal(18,6)    NOT NULL,
        AcceptedQtyBase     decimal(18,6)    NOT NULL,
        RejectedQtyBase     decimal(18,6)    NOT NULL CONSTRAINT DF_GRNLine_Rejected DEFAULT 0,
        FreeQtyBase         decimal(18,6)    NOT NULL CONSTRAINT DF_GRNLine_Free DEFAULT 0,  -- bonus units (zero cost)
        BatchNo             nvarchar(64)     NULL,       -- required when product tracks batches
        MfgDate             date             NULL,
        ExpiryDate          date             NULL,       -- required when product tracks expiry
        BatchId             uniqueidentifier NULL,       -- resolved/created at posting
        LocationId          uniqueidentifier NOT NULL,
        UnitCost            decimal(19,4)    NOT NULL,   -- per UomId, before discount
        DiscountPercent     decimal(9,4)     NOT NULL CONSTRAINT DF_GRNLine_Disc DEFAULT 0,
        TaxCodeId           uniqueidentifier NULL,
        TaxRatePercent      decimal(9,4)     NOT NULL CONSTRAINT DF_GRNLine_TaxRate DEFAULT 0,
        TaxAmount           decimal(19,4)    NOT NULL CONSTRAINT DF_GRNLine_TaxAmt DEFAULT 0,
        LineTotal           decimal(19,4)    NOT NULL,
        RejectReason        nvarchar(300)    NULL,
        CONSTRAINT PK_GoodsReceiptLine PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_GRNLine_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_GRNLine_LineNo UNIQUE NONCLUSTERED (TenantId, GoodsReceiptId, LineNumber),
        CONSTRAINT FK_GRNLine_GRN FOREIGN KEY (TenantId, GoodsReceiptId) REFERENCES purchasing.GoodsReceipt (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_GRNLine_POLine FOREIGN KEY (TenantId, PurchaseOrderLineId) REFERENCES purchasing.PurchaseOrderLine (TenantId, Id),
        CONSTRAINT FK_GRNLine_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_GRNLine_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT FK_GRNLine_Batch FOREIGN KEY (TenantId, BatchId) REFERENCES inventory.Batch (TenantId, Id),
        CONSTRAINT FK_GRNLine_Location FOREIGN KEY (TenantId, LocationId) REFERENCES inventory.Location (TenantId, Id),
        CONSTRAINT FK_GRNLine_TaxCode FOREIGN KEY (TenantId, TaxCodeId) REFERENCES catalog.TaxCode (TenantId, Id),
        CONSTRAINT CK_GRNLine_Qty CHECK (Quantity > 0 AND QtyBase > 0 AND AcceptedQtyBase >= 0 AND RejectedQtyBase >= 0 AND FreeQtyBase >= 0
                                         AND AcceptedQtyBase + RejectedQtyBase = QtyBase),
        CONSTRAINT CK_GRNLine_Cost CHECK (UnitCost >= 0 AND DiscountPercent BETWEEN 0 AND 100)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_GoodsReceiptLine ON purchasing.GoodsReceiptLine (TenantId, ClusterKey);

    CREATE TABLE purchasing.PurchaseReturn
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_PRet_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        DocumentNo          nvarchar(40)     NULL,
        SupplierId          uniqueidentifier NOT NULL,
        GoodsReceiptId      uniqueidentifier NULL,
        WarehouseId         uniqueidentifier NOT NULL,
        BusinessDate        date             NOT NULL,
        Status              varchar(20)      NOT NULL CONSTRAINT DF_PRet_Status DEFAULT 'Draft',
        ReasonCodeId        uniqueidentifier NULL,
        TotalAmount         decimal(19,4)    NOT NULL CONSTRAINT DF_PRet_Total DEFAULT 0, -- debit note value
        Notes               nvarchar(1000)   NULL,
        ClientDocumentId    uniqueidentifier NULL,
        PostedBy            uniqueidentifier NULL,
        PostedAtUtc         datetime2(3)     NULL,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_PRet_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_PurchaseReturn PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_PRet_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT FK_PRet_Supplier FOREIGN KEY (TenantId, SupplierId) REFERENCES purchasing.Supplier (TenantId, Id),
        CONSTRAINT FK_PRet_GRN FOREIGN KEY (TenantId, GoodsReceiptId) REFERENCES purchasing.GoodsReceipt (TenantId, Id),
        CONSTRAINT FK_PRet_Warehouse FOREIGN KEY (TenantId, WarehouseId) REFERENCES inventory.Warehouse (TenantId, Id),
        CONSTRAINT FK_PRet_Reason FOREIGN KEY (TenantId, ReasonCodeId) REFERENCES inventory.ReasonCode (TenantId, Id),
        CONSTRAINT CK_PRet_Status CHECK (Status IN ('Draft','Posted','Cancelled'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_PurchaseReturn ON purchasing.PurchaseReturn (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_PRet_No ON purchasing.PurchaseReturn (TenantId, DocumentNo) WHERE DocumentNo IS NOT NULL;
    CREATE UNIQUE INDEX UX_PRet_ClientId ON purchasing.PurchaseReturn (TenantId, ClientDocumentId) WHERE ClientDocumentId IS NOT NULL;

    CREATE TABLE purchasing.PurchaseReturnLine
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_PRetLine_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        PurchaseReturnId    uniqueidentifier NOT NULL,
        LineNumber              int              NOT NULL,
        GoodsReceiptLineId  uniqueidentifier NULL,
        ProductId           uniqueidentifier NOT NULL,
        UomId               uniqueidentifier NOT NULL,
        Quantity            decimal(18,6)    NOT NULL,
        QtyBase             decimal(18,6)    NOT NULL,
        BatchId             uniqueidentifier NOT NULL,
        LocationId          uniqueidentifier NOT NULL,
        UnitCost            decimal(19,4)    NOT NULL,
        LineTotal           decimal(19,4)    NOT NULL,
        CONSTRAINT PK_PurchaseReturnLine PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_PRetLine_LineNo UNIQUE NONCLUSTERED (TenantId, PurchaseReturnId, LineNumber),
        CONSTRAINT FK_PRetLine_Return FOREIGN KEY (TenantId, PurchaseReturnId) REFERENCES purchasing.PurchaseReturn (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_PRetLine_GRNLine FOREIGN KEY (TenantId, GoodsReceiptLineId) REFERENCES purchasing.GoodsReceiptLine (TenantId, Id),
        CONSTRAINT FK_PRetLine_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_PRetLine_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT FK_PRetLine_Batch FOREIGN KEY (TenantId, BatchId) REFERENCES inventory.Batch (TenantId, Id),
        CONSTRAINT FK_PRetLine_Location FOREIGN KEY (TenantId, LocationId) REFERENCES inventory.Location (TenantId, Id),
        CONSTRAINT CK_PRetLine_Qty CHECK (Quantity > 0 AND QtyBase > 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_PurchaseReturnLine ON purchasing.PurchaseReturnLine (TenantId, ClusterKey);

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'005_Purchasing');
    COMMIT TRANSACTION;
    PRINT N'005_Purchasing applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
