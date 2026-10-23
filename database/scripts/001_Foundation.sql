/*
  001_Foundation.sql
  Run connected to [StockOrderPlatform].
  Creates: dbo.SchemaVersion, all module schemas, tenancy tables, platform tables
  (number series, audit log, outbox/inbox, idempotency, attachments, approvals, notifications).

  Script conventions (all numbered scripts follow them):
  - One batch, one transaction: either everything applies or nothing does.
  - Re-running is safe: the script exits immediately if it is recorded in dbo.SchemaVersion.
  - Every tenant-owned table has TenantId and is protected by row-level security (script 010).
  - Ids are uniqueidentifier (clients can create them offline). Physical order is
    (TenantId, ClusterKey) to keep inserts sequential and tenant data together.
  - Quantities decimal(18,6); money decimal(19,4); unit costs decimal(19,6); times datetime2(3) UTC.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- required for filtered indexes; sqlcmd defaults it OFF
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.SchemaVersion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SchemaVersion
    (
        ScriptName    nvarchar(200) NOT NULL CONSTRAINT PK_SchemaVersion PRIMARY KEY,
        AppliedAtUtc  datetime2(3)  NOT NULL CONSTRAINT DF_SchemaVersion_AppliedAtUtc DEFAULT SYSUTCDATETIME(),
        AppliedBy     nvarchar(128) NOT NULL CONSTRAINT DF_SchemaVersion_AppliedBy DEFAULT SUSER_SNAME()
    );
END;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'001_Foundation')
BEGIN
    PRINT N'001_Foundation already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Module schemas
    IF SCHEMA_ID(N'tenancy')    IS NULL EXEC(N'CREATE SCHEMA tenancy AUTHORIZATION dbo');
    IF SCHEMA_ID(N'identity')   IS NULL EXEC(N'CREATE SCHEMA [identity] AUTHORIZATION dbo');
    IF SCHEMA_ID(N'catalog')    IS NULL EXEC(N'CREATE SCHEMA catalog AUTHORIZATION dbo');
    IF SCHEMA_ID(N'inventory')  IS NULL EXEC(N'CREATE SCHEMA inventory AUTHORIZATION dbo');
    IF SCHEMA_ID(N'purchasing') IS NULL EXEC(N'CREATE SCHEMA purchasing AUTHORIZATION dbo');
    IF SCHEMA_ID(N'sales')      IS NULL EXEC(N'CREATE SCHEMA sales AUTHORIZATION dbo');
    IF SCHEMA_ID(N'finance')    IS NULL EXEC(N'CREATE SCHEMA finance AUTHORIZATION dbo');
    IF SCHEMA_ID(N'platform')   IS NULL EXEC(N'CREATE SCHEMA platform AUTHORIZATION dbo');
    IF SCHEMA_ID(N'sync')       IS NULL EXEC(N'CREATE SCHEMA sync AUTHORIZATION dbo');
    IF SCHEMA_ID(N'security')   IS NULL EXEC(N'CREATE SCHEMA security AUTHORIZATION dbo');

    /* ---------------------------------------------------------------- tenancy */

    CREATE TABLE tenancy.Tenant
    (
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Tenant_Id DEFAULT NEWSEQUENTIALID(),
        Code            nvarchar(32)     NOT NULL,   -- company code typed at login, e.g. 'KARAHI01'
        Name            nvarchar(200)    NOT NULL,
        Industry        varchar(20)      NOT NULL,   -- template applied at creation
        BaseCurrency    char(3)          NOT NULL CONSTRAINT DF_Tenant_BaseCurrency DEFAULT 'PKR',
        TimeZoneId      varchar(64)      NOT NULL CONSTRAINT DF_Tenant_TimeZoneId DEFAULT 'Asia/Karachi',
        Status          varchar(20)      NOT NULL CONSTRAINT DF_Tenant_Status DEFAULT 'Active',
        IsPlatform      bit              NOT NULL CONSTRAINT DF_Tenant_IsPlatform DEFAULT 0, -- the operator's own tenant
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Tenant_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc    datetime2(3)     NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_Tenant PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_Tenant_Code UNIQUE (Code),
        CONSTRAINT CK_Tenant_Industry CHECK (Industry IN ('Restaurant','Pharma','Retail','Platform')),
        CONSTRAINT CK_Tenant_Status CHECK (Status IN ('Active','Suspended','Closed'))
    );

    CREATE TABLE tenancy.Branch
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Branch_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        Code            nvarchar(32)     NOT NULL,
        Name            nvarchar(200)    NOT NULL,
        Address         nvarchar(500)    NULL,
        Phone           nvarchar(40)     NULL,
        IsActive        bit              NOT NULL CONSTRAINT DF_Branch_IsActive DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Branch_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_Branch PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Branch_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_Branch_Code UNIQUE NONCLUSTERED (TenantId, Code),
        CONSTRAINT FK_Branch_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Branch ON tenancy.Branch (TenantId, ClusterKey);

    -- Feature flags per tenant (keys listed in the Build Spec, e.g. 'BatchTracking', 'Restaurant.Tables')
    CREATE TABLE tenancy.TenantFeature
    (
        TenantId        uniqueidentifier NOT NULL,
        FeatureKey      varchar(100)     NOT NULL,
        IsEnabled       bit              NOT NULL,
        UpdatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_TenantFeature_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_TenantFeature PRIMARY KEY CLUSTERED (TenantId, FeatureKey),
        CONSTRAINT FK_TenantFeature_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id)
    );

    -- Tenant settings: terminology overrides, defaults, policies (value is text or JSON)
    CREATE TABLE tenancy.TenantSetting
    (
        TenantId        uniqueidentifier NOT NULL,
        SettingKey      varchar(100)     NOT NULL,
        Value           nvarchar(max)    NOT NULL,
        UpdatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_TenantSetting_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_TenantSetting PRIMARY KEY CLUSTERED (TenantId, SettingKey),
        CONSTRAINT FK_TenantSetting_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id)
    );

    /* --------------------------------------------------------------- platform */

    -- Document numbering. Next number is taken atomically:
    -- UPDATE ... SET NextNumber = NextNumber + 1 OUTPUT deleted.NextNumber WHERE ...
    CREATE TABLE platform.NumberSeries
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_NumberSeries_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        DocumentType    varchar(40)      NOT NULL,   -- e.g. 'SalesOrder', 'Invoice', 'GoodsReceipt'
        BranchId        uniqueidentifier NULL,       -- NULL = one series for the whole tenant
        Prefix          nvarchar(20)     NOT NULL,   -- e.g. 'INV-'
        NextNumber      bigint           NOT NULL CONSTRAINT DF_NumberSeries_NextNumber DEFAULT 1,
        PaddingLength   tinyint          NOT NULL CONSTRAINT DF_NumberSeries_Padding DEFAULT 6,
        ResetPolicy     varchar(10)      NOT NULL CONSTRAINT DF_NumberSeries_Reset DEFAULT 'Never',
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_NumberSeries PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_NumberSeries_Reset CHECK (ResetPolicy IN ('Never','Yearly','Monthly')),
        CONSTRAINT FK_NumberSeries_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_NumberSeries ON platform.NumberSeries (TenantId, ClusterKey);
    CREATE UNIQUE INDEX UX_NumberSeries_Type ON platform.NumberSeries (TenantId, DocumentType, BranchId);

    -- Append-only audit trail (UPDATE/DELETE blocked by trigger below)
    CREATE TABLE platform.AuditLog
    (
        Id              bigint IDENTITY(1,1) NOT NULL,
        TenantId        uniqueidentifier NOT NULL,
        UserId          uniqueidentifier NULL,
        EntityName      varchar(100)     NOT NULL,
        EntityId        nvarchar(64)     NOT NULL,
        Action          varchar(20)      NOT NULL,   -- Created, Updated, Deleted, Posted, Approved, Cancelled, Login...
        OldValues       nvarchar(max)    NULL,       -- JSON
        NewValues       nvarchar(max)    NULL,       -- JSON
        CorrelationId   varchar(64)      NULL,
        IpAddress       varchar(45)      NULL,
        AtUtc           datetime2(3)     NOT NULL CONSTRAINT DF_AuditLog_AtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AuditLog PRIMARY KEY NONCLUSTERED (Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_AuditLog ON platform.AuditLog (TenantId, Id);
    CREATE INDEX IX_AuditLog_Entity ON platform.AuditLog (TenantId, EntityName, EntityId);

    -- Transactional outbox: written in the same transaction as the business change,
    -- dispatched by the worker service. TenantId NULL = platform-level event.
    CREATE TABLE platform.OutboxMessage
    (
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Outbox_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        TenantId        uniqueidentifier NULL,
        MessageType     varchar(200)     NOT NULL,
        Payload         nvarchar(max)    NOT NULL,
        OccurredAtUtc   datetime2(3)     NOT NULL CONSTRAINT DF_Outbox_OccurredAtUtc DEFAULT SYSUTCDATETIME(),
        ProcessedAtUtc  datetime2(3)     NULL,
        Attempts        int              NOT NULL CONSTRAINT DF_Outbox_Attempts DEFAULT 0,
        LastError       nvarchar(2000)   NULL,
        CONSTRAINT PK_OutboxMessage PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_Outbox_Payload_Json CHECK (ISJSON(Payload) = 1)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_OutboxMessage ON platform.OutboxMessage (ClusterKey);
    CREATE INDEX IX_OutboxMessage_Pending ON platform.OutboxMessage (ClusterKey) WHERE ProcessedAtUtc IS NULL;

    -- Inbox: makes every consumer idempotent (a message is handled once per consumer)
    CREATE TABLE platform.InboxMessage
    (
        MessageId       uniqueidentifier NOT NULL,
        Consumer        varchar(200)     NOT NULL,
        ProcessedAtUtc  datetime2(3)     NOT NULL CONSTRAINT DF_Inbox_ProcessedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_InboxMessage PRIMARY KEY CLUSTERED (MessageId, Consumer)
    );

    -- Idempotency-Key header records for write APIs
    CREATE TABLE platform.IdempotencyRecord
    (
        TenantId        uniqueidentifier NOT NULL,
        IdempotencyKey  nvarchar(100)    NOT NULL,
        UserId          uniqueidentifier NULL,
        RequestPath     nvarchar(400)    NOT NULL,
        RequestHash     varchar(64)      NOT NULL,   -- SHA-256 hex of method + path + body
        ResponseStatus  int              NULL,
        ResponseBody    nvarchar(max)    NULL,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Idem_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        ExpiresAtUtc    datetime2(3)     NOT NULL,
        CONSTRAINT PK_IdempotencyRecord PRIMARY KEY CLUSTERED (TenantId, IdempotencyKey)
    );
    CREATE INDEX IX_IdempotencyRecord_Expires ON platform.IdempotencyRecord (ExpiresAtUtc);

    -- Files stored on disk (NTFS folder); this row holds the metadata
    CREATE TABLE platform.Attachment
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Attachment_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        FileName        nvarchar(260)    NOT NULL,
        ContentType     varchar(100)     NOT NULL,
        SizeBytes       bigint           NOT NULL,
        StoragePath     nvarchar(500)    NOT NULL,   -- relative to the configured file root
        Sha256          varchar(64)      NULL,
        EntityName      varchar(100)     NULL,
        EntityId        uniqueidentifier NULL,
        UploadedBy      uniqueidentifier NULL,
        UploadedAtUtc   datetime2(3)     NOT NULL CONSTRAINT DF_Attachment_UploadedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_Attachment PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_Attachment_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Attachment ON platform.Attachment (TenantId, ClusterKey);
    CREATE INDEX IX_Attachment_Entity ON platform.Attachment (TenantId, EntityName, EntityId);

    -- Simple approvals (maker-checker) for documents above thresholds
    CREATE TABLE platform.ApprovalRequest
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Approval_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        DocumentType    varchar(40)      NOT NULL,
        DocumentId      uniqueidentifier NOT NULL,
        DocumentNo      nvarchar(40)     NULL,
        Amount          decimal(19,4)    NULL,
        RequiredPermission varchar(100)  NOT NULL,
        Status          varchar(20)      NOT NULL CONSTRAINT DF_Approval_Status DEFAULT 'Pending',
        RequestedBy     uniqueidentifier NOT NULL,
        RequestedAtUtc  datetime2(3)     NOT NULL CONSTRAINT DF_Approval_RequestedAtUtc DEFAULT SYSUTCDATETIME(),
        DecidedBy       uniqueidentifier NULL,
        DecidedAtUtc    datetime2(3)     NULL,
        Comment         nvarchar(1000)   NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_ApprovalRequest PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_Approval_Status CHECK (Status IN ('Pending','Approved','Rejected','Cancelled'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_ApprovalRequest ON platform.ApprovalRequest (TenantId, ClusterKey);
    CREATE INDEX IX_ApprovalRequest_Pending ON platform.ApprovalRequest (TenantId, Status) INCLUDE (DocumentType, DocumentId);

    -- In-app notifications (also pushed via FCM)
    CREATE TABLE platform.Notification
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_Notification_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        UserId          uniqueidentifier NOT NULL,
        Category        varchar(40)      NOT NULL,   -- LowStock, NearExpiry, ApprovalWaiting, OrderStatus...
        Title           nvarchar(200)    NOT NULL,
        Body            nvarchar(1000)   NULL,
        DataJson        nvarchar(max)    NULL,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_Notification_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        ReadAtUtc       datetime2(3)     NULL,
        CONSTRAINT PK_Notification PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_Notification_Data_Json CHECK (DataJson IS NULL OR ISJSON(DataJson) = 1)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_Notification ON platform.Notification (TenantId, ClusterKey);
    CREATE INDEX IX_Notification_User ON platform.Notification (TenantId, UserId, ReadAtUtc);

    -- Block UPDATE/DELETE on the audit log
    EXEC(N'CREATE TRIGGER platform.TR_AuditLog_AppendOnly ON platform.AuditLog
          INSTEAD OF UPDATE, DELETE AS
          BEGIN
              SET NOCOUNT ON;
              THROW 50001, N''platform.AuditLog is append-only.'', 1;
          END');

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'001_Foundation');
    COMMIT TRANSACTION;
    PRINT N'001_Foundation applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
