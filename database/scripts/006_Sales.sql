/*
  006_Sales.sql
  Customer groups, customers, sales orders (one state machine for every channel),
  order status history, invoices, sales returns.
  Restaurant-specific columns (table, kitchen status, modifiers) are added by the
  restaurant pack scripts in Phase 2; pharma-specific ones (schemes, route) likewise.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'006_Sales')
BEGIN
    PRINT N'006_Sales already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE sales.CustomerGroup
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_CustGroup_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        Name            nvarchar(100)    NOT NULL,
        PriceListId     uniqueidentifier NULL,
        IsActive        bit              NOT NULL CONSTRAINT DF_CustGroup_IsActive DEFAULT 1,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_CustomerGroup PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_CustGroup_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_CustGroup_Name UNIQUE NONCLUSTERED (TenantId, Name),
        CONSTRAINT FK_CustGroup_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT FK_CustGroup_PriceList FOREIGN KEY (TenantId, PriceListId) REFERENCES catalog.PriceList (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_CustomerGroup ON sales.CustomerGroup (TenantId, ClusterKey);

    CREATE TABLE sales.Customer
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_Customer_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        Code                nvarchar(32)     NOT NULL,
        Name                nvarchar(200)    NOT NULL,
        ContactPerson       nvarchar(150)    NULL,
        Phone               nvarchar(40)     NULL,
        Email               nvarchar(256)    NULL,
        Address             nvarchar(500)    NULL,
        TaxNumber           nvarchar(40)     NULL,
        CustomerGroupId     uniqueidentifier NULL,
        PriceListId         uniqueidentifier NULL,      -- overrides the group's list
        CreditLimit         decimal(19,4)    NULL,       -- NULL = no credit check
        CreditDays          int              NULL,
        IsWalkIn            bit              NOT NULL CONSTRAINT DF_Customer_WalkIn DEFAULT 0, -- the one 'Walk-in customer' row
        CustomFieldsJson    nvarchar(max)    NULL,
        IsActive            bit              NOT NULL CONSTRAINT DF_Customer_IsActive DEFAULT 1,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_Customer_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_Customer PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Customer_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Customer_Code UNIQUE NONCLUSTERED (TenantId, Code),
        CONSTRAINT FK_Customer_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT FK_Customer_Group FOREIGN KEY (TenantId, CustomerGroupId) REFERENCES sales.CustomerGroup (TenantId, Id),
        CONSTRAINT FK_Customer_PriceList FOREIGN KEY (TenantId, PriceListId) REFERENCES catalog.PriceList (TenantId, Id),
        CONSTRAINT CK_Customer_Custom_Json CHECK (CustomFieldsJson IS NULL OR ISJSON(CustomFieldsJson) = 1),
        CONSTRAINT CK_Customer_Credit CHECK ((CreditLimit IS NULL OR CreditLimit >= 0) AND (CreditDays IS NULL OR CreditDays >= 0))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Customer ON sales.Customer (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_Customer_OneWalkIn ON sales.Customer (TenantId) WHERE IsWalkIn = 1;
    CREATE INDEX IX_Customer_Name ON sales.Customer (TenantId, Name);
    CREATE INDEX IX_Customer_Phone ON sales.Customer (TenantId, Phone);
    CREATE INDEX IX_Customer_Sync ON sales.Customer (TenantId, RowVer);

    CREATE TABLE sales.SalesOrder
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_SO_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        DocumentNo          nvarchar(40)     NULL,       -- assigned on confirmation
        DeviceDocumentNo    nvarchar(40)     NULL,       -- temporary number shown offline on the device
        Channel             varchar(20)      NOT NULL,
        CustomerId          uniqueidentifier NOT NULL,  -- walk-in customer row for anonymous sales
        BranchId            uniqueidentifier NOT NULL,
        WarehouseId         uniqueidentifier NOT NULL,
        OrderDate           datetime2(3)     NOT NULL,   -- UTC
        BusinessDate        date             NOT NULL,
        Status              varchar(20)      NOT NULL CONSTRAINT DF_SO_Status DEFAULT 'Draft',
        HoldReason          nvarchar(300)    NULL,
        PriceListId         uniqueidentifier NULL,
        Currency            char(3)          NOT NULL CONSTRAINT DF_SO_Currency DEFAULT 'PKR',
        SubTotal            decimal(19,4)    NOT NULL CONSTRAINT DF_SO_SubTotal DEFAULT 0,
        DiscountTotal       decimal(19,4)    NOT NULL CONSTRAINT DF_SO_DiscountTotal DEFAULT 0,
        TaxTotal            decimal(19,4)    NOT NULL CONSTRAINT DF_SO_TaxTotal DEFAULT 0,
        ChargesTotal        decimal(19,4)    NOT NULL CONSTRAINT DF_SO_ChargesTotal DEFAULT 0, -- service/delivery charges
        GrandTotal          decimal(19,4)    NOT NULL CONSTRAINT DF_SO_GrandTotal DEFAULT 0,
        PaidAmount          decimal(19,4)    NOT NULL CONSTRAINT DF_SO_Paid DEFAULT 0,
        BackOrderPolicy     varchar(20)      NOT NULL CONSTRAINT DF_SO_BackOrder DEFAULT 'Reject',
        DeliveryAddress     nvarchar(500)    NULL,
        Notes               nvarchar(1000)   NULL,
        CustomFieldsJson    nvarchar(max)    NULL,
        SalesRepUserId      uniqueidentifier NULL,
        ClientDocumentId    uniqueidentifier NULL,       -- idempotency for offline creation
        SourceDeviceId      uniqueidentifier NULL,
        CashSessionId       uniqueidentifier NULL,       -- FK added in 007
        ConfirmedAtUtc      datetime2(3)     NULL,
        DispatchedAtUtc     datetime2(3)     NULL,
        DeliveredAtUtc      datetime2(3)     NULL,
        CancelledAtUtc      datetime2(3)     NULL,
        CancelReasonCodeId  uniqueidentifier NULL,
        CancelNotes         nvarchar(500)    NULL,
        Version             int              NOT NULL CONSTRAINT DF_SO_Version DEFAULT 1, -- incremented on amendment
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_SO_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_SalesOrder PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_SO_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT FK_SO_Customer FOREIGN KEY (TenantId, CustomerId) REFERENCES sales.Customer (TenantId, Id),
        CONSTRAINT FK_SO_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id),
        CONSTRAINT FK_SO_Warehouse FOREIGN KEY (TenantId, WarehouseId) REFERENCES inventory.Warehouse (TenantId, Id),
        CONSTRAINT FK_SO_PriceList FOREIGN KEY (TenantId, PriceListId) REFERENCES catalog.PriceList (TenantId, Id),
        CONSTRAINT FK_SO_SalesRep FOREIGN KEY (TenantId, SalesRepUserId) REFERENCES [identity].[User] (TenantId, Id),
        CONSTRAINT FK_SO_CancelReason FOREIGN KEY (TenantId, CancelReasonCodeId) REFERENCES inventory.ReasonCode (TenantId, Id),
        CONSTRAINT CK_SO_Channel CHECK (Channel IN ('BackOffice','Counter','DineIn','Takeaway','Delivery','FieldBooking')),
        CONSTRAINT CK_SO_Status CHECK (Status IN ('Draft','Submitted','OnHold','Confirmed','Fulfilling','Dispatched',
                                                  'Delivered','Invoiced','Paid','Closed','Cancelled')),
        CONSTRAINT CK_SO_BackOrder CHECK (BackOrderPolicy IN ('Reject','BackOrder','ShipAvailable')),
        CONSTRAINT CK_SO_Custom_Json CHECK (CustomFieldsJson IS NULL OR ISJSON(CustomFieldsJson) = 1),
        CONSTRAINT CK_SO_Amounts CHECK (GrandTotal >= 0 AND PaidAmount >= 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_SalesOrder ON sales.SalesOrder (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_SO_No ON sales.SalesOrder (TenantId, DocumentNo) WHERE DocumentNo IS NOT NULL;
    CREATE UNIQUE INDEX UX_SO_ClientId ON sales.SalesOrder (TenantId, ClientDocumentId) WHERE ClientDocumentId IS NOT NULL;
    CREATE INDEX IX_SO_Customer ON sales.SalesOrder (TenantId, CustomerId, BusinessDate);
    CREATE INDEX IX_SO_Status ON sales.SalesOrder (TenantId, BranchId, Status, BusinessDate);
    CREATE INDEX IX_SO_Sync ON sales.SalesOrder (TenantId, RowVer);

    CREATE TABLE sales.SalesOrderLine
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_SOLine_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        SalesOrderId        uniqueidentifier NOT NULL,
        LineNumber          int              NOT NULL,
        ParentLineId        uniqueidentifier NULL,       -- modifiers / add-ons hang under a line (Phase 2)
        ProductId           uniqueidentifier NOT NULL,
        UomId               uniqueidentifier NOT NULL,
        Quantity            decimal(18,6)    NOT NULL,
        QtyBase             decimal(18,6)    NOT NULL,
        FulfilledQtyBase    decimal(18,6)    NOT NULL CONSTRAINT DF_SOLine_Fulfilled DEFAULT 0,
        BackOrderedQtyBase  decimal(18,6)    NOT NULL CONSTRAINT DF_SOLine_BackOrdered DEFAULT 0,
        UnitPrice           decimal(19,4)    NOT NULL,   -- snapshot at confirmation, per UomId
        DiscountPercent     decimal(9,4)     NOT NULL CONSTRAINT DF_SOLine_DiscPct DEFAULT 0,
        DiscountAmount      decimal(19,4)    NOT NULL CONSTRAINT DF_SOLine_DiscAmt DEFAULT 0,
        TaxCodeId           uniqueidentifier NULL,
        TaxRatePercent      decimal(9,4)     NOT NULL CONSTRAINT DF_SOLine_TaxRate DEFAULT 0,
        TaxAmount           decimal(19,4)    NOT NULL CONSTRAINT DF_SOLine_TaxAmt DEFAULT 0,
        LineTotal           decimal(19,4)    NOT NULL,
        UnitCostAtSale      decimal(19,6)    NULL,       -- for margin reports, set at issue
        Status              varchar(20)      NOT NULL CONSTRAINT DF_SOLine_Status DEFAULT 'Open',
        Notes               nvarchar(500)    NULL,
        CONSTRAINT PK_SalesOrderLine PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_SOLine_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_SOLine_LineNumber UNIQUE NONCLUSTERED (TenantId, SalesOrderId, LineNumber),
        CONSTRAINT FK_SOLine_SO FOREIGN KEY (TenantId, SalesOrderId) REFERENCES sales.SalesOrder (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_SOLine_Parent FOREIGN KEY (TenantId, ParentLineId) REFERENCES sales.SalesOrderLine (TenantId, Id),
        CONSTRAINT FK_SOLine_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_SOLine_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT FK_SOLine_TaxCode FOREIGN KEY (TenantId, TaxCodeId) REFERENCES catalog.TaxCode (TenantId, Id),
        CONSTRAINT CK_SOLine_Qty CHECK (Quantity > 0 AND QtyBase > 0 AND FulfilledQtyBase >= 0 AND BackOrderedQtyBase >= 0),
        CONSTRAINT CK_SOLine_Price CHECK (UnitPrice >= 0 AND DiscountPercent BETWEEN 0 AND 100 AND DiscountAmount >= 0),
        CONSTRAINT CK_SOLine_Status CHECK (Status IN ('Open','PartiallyFulfilled','Fulfilled','BackOrdered','Cancelled'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_SalesOrderLine ON sales.SalesOrderLine (TenantId, ClusterKey);
    CREATE INDEX IX_SOLine_Product ON sales.SalesOrderLine (TenantId, ProductId);

    CREATE TABLE sales.SalesOrderStatusHistory
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              bigint IDENTITY(1,1) NOT NULL,
        SalesOrderId    uniqueidentifier NOT NULL,
        FromStatus      varchar(20)      NULL,
        ToStatus        varchar(20)      NOT NULL,
        ChangedBy       uniqueidentifier NULL,
        ChangedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_SOHist_ChangedAtUtc DEFAULT SYSUTCDATETIME(),
        Reason          nvarchar(500)    NULL,
        CONSTRAINT PK_SalesOrderStatusHistory PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_SOHist_SO FOREIGN KEY (TenantId, SalesOrderId) REFERENCES sales.SalesOrder (TenantId, Id) ON DELETE CASCADE
    );
    CREATE UNIQUE CLUSTERED INDEX CX_SalesOrderStatusHistory ON sales.SalesOrderStatusHistory (TenantId, Id);
    CREATE INDEX IX_SOHist_SO ON sales.SalesOrderStatusHistory (TenantId, SalesOrderId);

    CREATE TABLE sales.Invoice
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_Invoice_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        DocumentNo          nvarchar(40)     NOT NULL,   -- assigned at posting, gap-free per series
        SalesOrderId        uniqueidentifier NULL,
        CustomerId          uniqueidentifier NOT NULL,
        BranchId            uniqueidentifier NOT NULL,
        InvoiceDate         date             NOT NULL,
        DueDate             date             NULL,
        Status              varchar(20)      NOT NULL CONSTRAINT DF_Invoice_Status DEFAULT 'Posted',
        Currency            char(3)          NOT NULL CONSTRAINT DF_Invoice_Currency DEFAULT 'PKR',
        SubTotal            decimal(19,4)    NOT NULL,
        DiscountTotal       decimal(19,4)    NOT NULL,
        TaxTotal            decimal(19,4)    NOT NULL,
        ChargesTotal        decimal(19,4)    NOT NULL CONSTRAINT DF_Invoice_Charges DEFAULT 0,
        GrandTotal          decimal(19,4)    NOT NULL,
        AmountPaid          decimal(19,4)    NOT NULL CONSTRAINT DF_Invoice_Paid DEFAULT 0,
        BalanceDue          AS (GrandTotal - AmountPaid) PERSISTED,
        PostedBy            uniqueidentifier NULL,
        PostedAtUtc         datetime2(3)     NOT NULL CONSTRAINT DF_Invoice_PostedAtUtc DEFAULT SYSUTCDATETIME(),
        CancelledAtUtc      datetime2(3)     NULL,
        CancelledBy         uniqueidentifier NULL,
        CancelReason        nvarchar(500)    NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_Invoice PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Invoice_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Invoice_No UNIQUE NONCLUSTERED (TenantId, DocumentNo),
        CONSTRAINT FK_Invoice_SO FOREIGN KEY (TenantId, SalesOrderId) REFERENCES sales.SalesOrder (TenantId, Id),
        CONSTRAINT FK_Invoice_Customer FOREIGN KEY (TenantId, CustomerId) REFERENCES sales.Customer (TenantId, Id),
        CONSTRAINT FK_Invoice_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id),
        CONSTRAINT CK_Invoice_Status CHECK (Status IN ('Posted','Cancelled')),
        CONSTRAINT CK_Invoice_Amounts CHECK (GrandTotal >= 0 AND AmountPaid >= 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Invoice ON sales.Invoice (TenantId, ClusterKey);
    CREATE INDEX IX_Invoice_Customer ON sales.Invoice (TenantId, CustomerId, InvoiceDate);
    CREATE INDEX IX_Invoice_Open ON sales.Invoice (TenantId, CustomerId) INCLUDE (GrandTotal, AmountPaid, DueDate) WHERE Status = 'Posted';

    CREATE TABLE sales.InvoiceLine
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_InvoiceLine_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        InvoiceId           uniqueidentifier NOT NULL,
        LineNumber          int              NOT NULL,
        SalesOrderLineId    uniqueidentifier NULL,
        ProductId           uniqueidentifier NOT NULL,
        Description         nvarchar(300)    NOT NULL,   -- product name snapshot
        UomId               uniqueidentifier NOT NULL,
        Quantity            decimal(18,6)    NOT NULL,
        QtyBase             decimal(18,6)    NOT NULL,
        UnitPrice           decimal(19,4)    NOT NULL,
        DiscountAmount      decimal(19,4)    NOT NULL CONSTRAINT DF_InvoiceLine_Disc DEFAULT 0,
        TaxCodeId           uniqueidentifier NULL,
        TaxRatePercent      decimal(9,4)     NOT NULL CONSTRAINT DF_InvoiceLine_TaxRate DEFAULT 0,
        TaxAmount           decimal(19,4)    NOT NULL CONSTRAINT DF_InvoiceLine_TaxAmt DEFAULT 0,
        LineTotal           decimal(19,4)    NOT NULL,
        CONSTRAINT PK_InvoiceLine PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_InvoiceLine_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_InvoiceLine_LineNumber UNIQUE NONCLUSTERED (TenantId, InvoiceId, LineNumber),
        CONSTRAINT FK_InvoiceLine_Invoice FOREIGN KEY (TenantId, InvoiceId) REFERENCES sales.Invoice (TenantId, Id),
        CONSTRAINT FK_InvoiceLine_SOLine FOREIGN KEY (TenantId, SalesOrderLineId) REFERENCES sales.SalesOrderLine (TenantId, Id),
        CONSTRAINT FK_InvoiceLine_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_InvoiceLine_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT FK_InvoiceLine_TaxCode FOREIGN KEY (TenantId, TaxCodeId) REFERENCES catalog.TaxCode (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_InvoiceLine ON sales.InvoiceLine (TenantId, ClusterKey);

    CREATE TABLE sales.SalesReturn
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_SRet_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        DocumentNo          nvarchar(40)     NULL,
        InvoiceId           uniqueidentifier NOT NULL,  -- returns are always against an invoice
        CustomerId          uniqueidentifier NOT NULL,
        WarehouseId         uniqueidentifier NOT NULL,
        BusinessDate        date             NOT NULL,
        Status              varchar(20)      NOT NULL CONSTRAINT DF_SRet_Status DEFAULT 'Draft',
        ReasonCodeId        uniqueidentifier NULL,
        CreditAmount        decimal(19,4)    NOT NULL CONSTRAINT DF_SRet_Credit DEFAULT 0, -- credit note value
        RefundMethod        varchar(20)      NOT NULL CONSTRAINT DF_SRet_Refund DEFAULT 'Credit',
        Notes               nvarchar(1000)   NULL,
        ClientDocumentId    uniqueidentifier NULL,
        SourceDeviceId      uniqueidentifier NULL,
        PostedBy            uniqueidentifier NULL,
        PostedAtUtc         datetime2(3)     NULL,
        CreatedAtUtc        datetime2(3)     NOT NULL CONSTRAINT DF_SRet_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           uniqueidentifier NULL,
        UpdatedAtUtc        datetime2(3)     NULL,
        UpdatedBy           uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_SalesReturn PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_SRet_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT FK_SRet_Invoice FOREIGN KEY (TenantId, InvoiceId) REFERENCES sales.Invoice (TenantId, Id),
        CONSTRAINT FK_SRet_Customer FOREIGN KEY (TenantId, CustomerId) REFERENCES sales.Customer (TenantId, Id),
        CONSTRAINT FK_SRet_Warehouse FOREIGN KEY (TenantId, WarehouseId) REFERENCES inventory.Warehouse (TenantId, Id),
        CONSTRAINT FK_SRet_Reason FOREIGN KEY (TenantId, ReasonCodeId) REFERENCES inventory.ReasonCode (TenantId, Id),
        CONSTRAINT CK_SRet_Status CHECK (Status IN ('Draft','Posted','Cancelled')),
        CONSTRAINT CK_SRet_Refund CHECK (RefundMethod IN ('Credit','Cash','Original'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_SalesReturn ON sales.SalesReturn (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_SRet_No ON sales.SalesReturn (TenantId, DocumentNo) WHERE DocumentNo IS NOT NULL;
    CREATE UNIQUE INDEX UX_SRet_ClientId ON sales.SalesReturn (TenantId, ClientDocumentId) WHERE ClientDocumentId IS NOT NULL;

    CREATE TABLE sales.SalesReturnLine
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_SRetLine_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        SalesReturnId       uniqueidentifier NOT NULL,
        LineNumber          int              NOT NULL,
        InvoiceLineId       uniqueidentifier NOT NULL,
        ProductId           uniqueidentifier NOT NULL,
        UomId               uniqueidentifier NOT NULL,
        Quantity            decimal(18,6)    NOT NULL,
        QtyBase             decimal(18,6)    NOT NULL,
        BatchId             uniqueidentifier NOT NULL,  -- the batch originally issued
        Disposition         varchar(20)      NOT NULL,   -- where the returned stock goes
        ToLocationId        uniqueidentifier NULL,       -- NULL when written off
        UnitPrice           decimal(19,4)    NOT NULL,   -- from the invoice line
        LineTotal           decimal(19,4)    NOT NULL,
        CONSTRAINT PK_SalesReturnLine PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_SRetLine_LineNumber UNIQUE NONCLUSTERED (TenantId, SalesReturnId, LineNumber),
        CONSTRAINT FK_SRetLine_Return FOREIGN KEY (TenantId, SalesReturnId) REFERENCES sales.SalesReturn (TenantId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_SRetLine_InvoiceLine FOREIGN KEY (TenantId, InvoiceLineId) REFERENCES sales.InvoiceLine (TenantId, Id),
        CONSTRAINT FK_SRetLine_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT FK_SRetLine_Uom FOREIGN KEY (TenantId, UomId) REFERENCES catalog.UnitOfMeasure (TenantId, Id),
        CONSTRAINT FK_SRetLine_Batch FOREIGN KEY (TenantId, BatchId) REFERENCES inventory.Batch (TenantId, Id),
        CONSTRAINT FK_SRetLine_Location FOREIGN KEY (TenantId, ToLocationId) REFERENCES inventory.Location (TenantId, Id),
        CONSTRAINT CK_SRetLine_Qty CHECK (Quantity > 0 AND QtyBase > 0),
        CONSTRAINT CK_SRetLine_Disposition CHECK (Disposition IN ('Sellable','Quarantine','WriteOff')),
        CONSTRAINT CK_SRetLine_Location CHECK (Disposition = 'WriteOff' OR ToLocationId IS NOT NULL)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_SalesReturnLine ON sales.SalesReturnLine (TenantId, ClusterKey);

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'006_Sales');
    COMMIT TRANSACTION;
    PRINT N'006_Sales applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
