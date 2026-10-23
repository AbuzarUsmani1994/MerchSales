/*
  007_Finance.sql
  Payment methods, cash sessions (shifts), payments and their allocation to invoices,
  the append-only customer/supplier ledger, accounting periods (lock posting into
  closed periods). This is NOT a general ledger; full accounting is out of scope.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'007_Finance')
BEGIN
    PRINT N'007_Finance already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE finance.PaymentMethod
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_PayMethod_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        Code            nvarchar(20)     NOT NULL,   -- 'CASH', 'CARD', 'BANK', 'JAZZCASH', 'CREDIT'
        Name            nvarchar(100)    NOT NULL,
        Kind            varchar(20)      NOT NULL,
        IsActive        bit              NOT NULL CONSTRAINT DF_PayMethod_IsActive DEFAULT 1,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_PaymentMethod PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_PayMethod_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_PayMethod_Code UNIQUE NONCLUSTERED (TenantId, Code),
        CONSTRAINT FK_PayMethod_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_PayMethod_Kind CHECK (Kind IN ('Cash','Card','BankTransfer','Wallet','Credit','Cheque'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_PaymentMethod ON finance.PaymentMethod (TenantId, ClusterKey);
    CREATE INDEX IX_PaymentMethod_Sync ON finance.PaymentMethod (TenantId, RowVer);

    -- A cashier's shift. Its BusinessDate (not the clock) decides which day a sale belongs to.
    CREATE TABLE finance.CashSession
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_CashSession_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        BranchId            uniqueidentifier NOT NULL,
        DeviceId            uniqueidentifier NULL,
        BusinessDate        date             NOT NULL,
        Status              varchar(10)      NOT NULL CONSTRAINT DF_CashSession_Status DEFAULT 'Open',
        OpenedBy            uniqueidentifier NOT NULL,
        OpenedAtUtc         datetime2(3)     NOT NULL CONSTRAINT DF_CashSession_OpenedAtUtc DEFAULT SYSUTCDATETIME(),
        OpeningFloat        decimal(19,4)    NOT NULL CONSTRAINT DF_CashSession_Float DEFAULT 0,
        ClosedBy            uniqueidentifier NULL,
        ClosedAtUtc         datetime2(3)     NULL,
        ExpectedCash        decimal(19,4)    NULL,       -- computed at close
        CountedCash         decimal(19,4)    NULL,
        Variance            AS (CountedCash - ExpectedCash),
        ClientDocumentId    uniqueidentifier NULL,
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_CashSession PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_CashSession_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT FK_CashSession_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id),
        CONSTRAINT FK_CashSession_OpenedBy FOREIGN KEY (TenantId, OpenedBy) REFERENCES [identity].[User] (TenantId, Id),
        CONSTRAINT CK_CashSession_Status CHECK (Status IN ('Open','Closed'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_CashSession ON finance.CashSession (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_CashSession_ClientId ON finance.CashSession (TenantId, ClientDocumentId) WHERE ClientDocumentId IS NOT NULL;
    CREATE INDEX IX_CashSession_Open ON finance.CashSession (TenantId, BranchId, Status);

    ALTER TABLE sales.SalesOrder ADD CONSTRAINT FK_SO_CashSession
        FOREIGN KEY (TenantId, CashSessionId) REFERENCES finance.CashSession (TenantId, Id);

    CREATE TABLE finance.Payment
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_Payment_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        DocumentNo          nvarchar(40)     NULL,
        Direction           varchar(3)       NOT NULL,   -- In = received from customer, Out = paid to supplier / refund
        PartyType           varchar(10)      NOT NULL,
        PartyId             uniqueidentifier NOT NULL,   -- sales.Customer or purchasing.Supplier
        PaymentMethodId     uniqueidentifier NOT NULL,
        Amount              decimal(19,4)    NOT NULL,
        Currency            char(3)          NOT NULL CONSTRAINT DF_Payment_Currency DEFAULT 'PKR',
        PaymentDate         date             NOT NULL,
        Reference           nvarchar(100)    NULL,       -- card slip, bank ref, cheque no
        Status              varchar(20)      NOT NULL CONSTRAINT DF_Payment_Status DEFAULT 'Posted',
        BranchId            uniqueidentifier NOT NULL,
        CashSessionId       uniqueidentifier NULL,
        CollectedByUserId   uniqueidentifier NULL,       -- rider / rep collecting cash
        ClientDocumentId    uniqueidentifier NULL,
        SourceDeviceId      uniqueidentifier NULL,
        Notes               nvarchar(500)    NULL,
        PostedBy            uniqueidentifier NULL,
        PostedAtUtc         datetime2(3)     NOT NULL CONSTRAINT DF_Payment_PostedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVer              rowversion       NOT NULL,
        CONSTRAINT PK_Payment PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Payment_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT FK_Payment_Method FOREIGN KEY (TenantId, PaymentMethodId) REFERENCES finance.PaymentMethod (TenantId, Id),
        CONSTRAINT FK_Payment_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id),
        CONSTRAINT FK_Payment_CashSession FOREIGN KEY (TenantId, CashSessionId) REFERENCES finance.CashSession (TenantId, Id),
        CONSTRAINT CK_Payment_Direction CHECK (Direction IN ('In','Out')),
        CONSTRAINT CK_Payment_PartyType CHECK (PartyType IN ('Customer','Supplier')),
        CONSTRAINT CK_Payment_Amount CHECK (Amount > 0),
        CONSTRAINT CK_Payment_Status CHECK (Status IN ('Posted','Cancelled'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Payment ON finance.Payment (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_Payment_No ON finance.Payment (TenantId, DocumentNo) WHERE DocumentNo IS NOT NULL;
    CREATE UNIQUE INDEX UX_Payment_ClientId ON finance.Payment (TenantId, ClientDocumentId) WHERE ClientDocumentId IS NOT NULL;
    CREATE INDEX IX_Payment_Party ON finance.Payment (TenantId, PartyType, PartyId, PaymentDate);

    -- Which invoices / goods receipts a payment settles
    CREATE TABLE finance.PaymentAllocation
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_PayAlloc_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        PaymentId       uniqueidentifier NOT NULL,
        DocumentType    varchar(40)      NOT NULL,   -- 'Invoice', 'GoodsReceipt', 'SalesReturn'
        DocumentId      uniqueidentifier NOT NULL,
        Amount          decimal(19,4)    NOT NULL,
        CONSTRAINT PK_PaymentAllocation PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_PayAlloc_Payment FOREIGN KEY (TenantId, PaymentId) REFERENCES finance.Payment (TenantId, Id),
        CONSTRAINT CK_PayAlloc_Amount CHECK (Amount > 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_PaymentAllocation ON finance.PaymentAllocation (TenantId, ClusterKey);
    CREATE INDEX IX_PayAlloc_Document ON finance.PaymentAllocation (TenantId, DocumentType, DocumentId);

    -- Customer and supplier balances. Debit increases what the party owes us. Append-only.
    CREATE TABLE finance.PartyLedgerEntry
    (
        Id              bigint IDENTITY(1,1) NOT NULL,
        TenantId        uniqueidentifier NOT NULL,
        PartyType       varchar(10)      NOT NULL,
        PartyId         uniqueidentifier NOT NULL,
        DocumentType    varchar(40)      NOT NULL,
        DocumentId      uniqueidentifier NOT NULL,
        DocumentNo      nvarchar(40)     NULL,
        Debit           decimal(19,4)    NOT NULL CONSTRAINT DF_PartyLedger_Debit DEFAULT 0,
        Credit          decimal(19,4)    NOT NULL CONSTRAINT DF_PartyLedger_Credit DEFAULT 0,
        BusinessDate    date             NOT NULL,
        PostedAtUtc     datetime2(3)     NOT NULL CONSTRAINT DF_PartyLedger_PostedAtUtc DEFAULT SYSUTCDATETIME(),
        PostedBy        uniqueidentifier NULL,
        CONSTRAINT PK_PartyLedgerEntry PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_PartyLedger_PartyType CHECK (PartyType IN ('Customer','Supplier')),
        CONSTRAINT CK_PartyLedger_Amounts CHECK (Debit >= 0 AND Credit >= 0 AND (Debit > 0 OR Credit > 0))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_PartyLedgerEntry ON finance.PartyLedgerEntry (TenantId, Id);
    CREATE INDEX IX_PartyLedger_Party ON finance.PartyLedgerEntry (TenantId, PartyType, PartyId, BusinessDate) INCLUDE (Debit, Credit);

    -- Posting into a Closed period is rejected by the API (backdating rule)
    CREATE TABLE finance.AccountingPeriod
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Period_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        StartDate       date             NOT NULL,
        EndDate         date             NOT NULL,
        Status          varchar(10)      NOT NULL CONSTRAINT DF_Period_Status DEFAULT 'Open',
        ClosedBy        uniqueidentifier NULL,
        ClosedAtUtc     datetime2(3)     NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_AccountingPeriod PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_Period_Start UNIQUE NONCLUSTERED (TenantId, StartDate),
        CONSTRAINT FK_Period_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_Period_Dates CHECK (EndDate >= StartDate),
        CONSTRAINT CK_Period_Status CHECK (Status IN ('Open','Closed'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_AccountingPeriod ON finance.AccountingPeriod (TenantId, ClusterKey);

    EXEC(N'CREATE TRIGGER finance.TR_PartyLedgerEntry_AppendOnly ON finance.PartyLedgerEntry
          INSTEAD OF UPDATE, DELETE AS
          BEGIN
              SET NOCOUNT ON;
              THROW 50003, N''finance.PartyLedgerEntry is append-only. Post a reversing entry instead.'', 1;
          END');

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'007_Finance');
    COMMIT TRANSACTION;
    PRINT N'007_Finance applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
