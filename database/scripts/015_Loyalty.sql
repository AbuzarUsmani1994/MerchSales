/*
  015_Loyalty.sql
  Visit rewards for restaurants, agreed with the owner (not in the original Build Spec; see
  docs/decisions.md, "Visit rewards"):

  - A member is a customer with a phone number. Every paid bill of at least MinVisitBill counts
    as one visit (at most one per member per day); after VisitsForReward visits the member gets
    RewardAmount off a bill of at least MinRewardBill. Visits lapse after ExpiryMonths without one.
  - The programme is off until the owner switches it on per branch (sales.LoyaltyBranch.IsEnabled).
  - Owners and managers (new permission Sales.Loyalty.Manage) add members; cashiers can too, but
    only at a branch where a manager has switched on CashierCanAddMembers.

  New tables:
    sales.LoyaltyMember  members (phone unique per company)
    sales.LoyaltyVisit   append-only log of counted visits and used rewards (UPDATE/DELETE denied)
    sales.LoyaltyRule    the company's numbers (one row; the API uses the agreed defaults until saved)
    sales.LoyaltyBranch  the two switches per branch
  Plus sales.SalesOrder.LoyaltyPhone: the phone a waiter noted on an order, for the cashier.

  Run after 014. Safe to run once; re-running is skipped.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'015_Loyalty')
BEGIN
    PRINT N'015_Loyalty already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE sales.LoyaltyMember
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_LoyaltyMember_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        CustomerId      uniqueidentifier NOT NULL,
        Phone           varchar(15)      NOT NULL,   -- digits only, country code first: 923001234567
        Status          varchar(10)      NOT NULL CONSTRAINT DF_LoyaltyMember_Status DEFAULT 'Active',
        JoinedAtUtc     datetime2(3)     NOT NULL CONSTRAINT DF_LoyaltyMember_JoinedAtUtc DEFAULT SYSUTCDATETIME(),
        JoinedBy        uniqueidentifier NULL,
        JoinedBranchId  uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_LoyaltyMember PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_LoyaltyMember_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_LoyaltyMember_Phone UNIQUE NONCLUSTERED (TenantId, Phone),
        CONSTRAINT UX_LoyaltyMember_Customer UNIQUE NONCLUSTERED (TenantId, CustomerId),
        CONSTRAINT FK_LoyaltyMember_Customer FOREIGN KEY (TenantId, CustomerId) REFERENCES sales.Customer (TenantId, Id),
        CONSTRAINT FK_LoyaltyMember_Branch FOREIGN KEY (TenantId, JoinedBranchId) REFERENCES tenancy.Branch (TenantId, Id),
        CONSTRAINT CK_LoyaltyMember_Status CHECK (Status IN ('Active','Paused'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_LoyaltyMember ON sales.LoyaltyMember (TenantId, ClusterKey);

    CREATE TABLE sales.LoyaltyVisit
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_LoyaltyVisit_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        MemberId        uniqueidentifier NOT NULL,
        Kind            varchar(16)      NOT NULL,   -- Visit, RewardUsed (VisitReversed, RewardReturned: for sales returns, later)
        SalesOrderId    uniqueidentifier NOT NULL,
        BranchId        uniqueidentifier NOT NULL,
        BusinessDate    date             NOT NULL,
        BillAmount      decimal(19,4)    NOT NULL,   -- the bill before any reward
        RewardAmount    decimal(19,4)    NULL,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_LoyaltyVisit_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        CONSTRAINT PK_LoyaltyVisit PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_LoyaltyVisit_OrderKind UNIQUE NONCLUSTERED (TenantId, SalesOrderId, Kind),
        CONSTRAINT FK_LoyaltyVisit_Member FOREIGN KEY (TenantId, MemberId) REFERENCES sales.LoyaltyMember (TenantId, Id),
        CONSTRAINT FK_LoyaltyVisit_Order FOREIGN KEY (TenantId, SalesOrderId) REFERENCES sales.SalesOrder (TenantId, Id),
        CONSTRAINT FK_LoyaltyVisit_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id),
        CONSTRAINT CK_LoyaltyVisit_Kind CHECK (Kind IN ('Visit','RewardUsed','VisitReversed','RewardReturned'))
    );
    CREATE UNIQUE CLUSTERED INDEX CX_LoyaltyVisit ON sales.LoyaltyVisit (TenantId, ClusterKey);
    CREATE INDEX IX_LoyaltyVisit_Member ON sales.LoyaltyVisit (TenantId, MemberId, BusinessDate);

    CREATE TABLE sales.LoyaltyRule
    (
        TenantId        uniqueidentifier NOT NULL,
        VisitsForReward int              NOT NULL,
        MinVisitBill    decimal(19,4)    NOT NULL,
        RewardAmount    decimal(19,4)    NOT NULL,
        MinRewardBill   decimal(19,4)    NOT NULL,
        ExpiryMonths    int              NOT NULL,
        UpdatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_LoyaltyRule_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_LoyaltyRule PRIMARY KEY CLUSTERED (TenantId),
        CONSTRAINT FK_LoyaltyRule_Tenant FOREIGN KEY (TenantId) REFERENCES tenancy.Tenant (Id),
        CONSTRAINT CK_LoyaltyRule_Values CHECK (VisitsForReward BETWEEN 1 AND 100 AND MinVisitBill >= 0 AND RewardAmount > 0
            AND MinRewardBill >= RewardAmount AND ExpiryMonths BETWEEN 1 AND 60)
    );

    CREATE TABLE sales.LoyaltyBranch
    (
        TenantId                uniqueidentifier NOT NULL,
        BranchId                uniqueidentifier NOT NULL,
        IsEnabled               bit              NOT NULL CONSTRAINT DF_LoyaltyBranch_IsEnabled DEFAULT 0,
        CashierCanAddMembers    bit              NOT NULL CONSTRAINT DF_LoyaltyBranch_CashierCanAdd DEFAULT 0,
        UpdatedAtUtc            datetime2(3)     NOT NULL CONSTRAINT DF_LoyaltyBranch_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy               uniqueidentifier NULL,
        RowVer                  rowversion       NOT NULL,
        CONSTRAINT PK_LoyaltyBranch PRIMARY KEY CLUSTERED (TenantId, BranchId),
        CONSTRAINT FK_LoyaltyBranch_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id)
    );

    ALTER TABLE sales.SalesOrder ADD LoyaltyPhone varchar(15) NULL;

    -- The visit log is history: rows are only ever added (like the stock and money ledgers).
    DENY UPDATE, DELETE ON sales.LoyaltyVisit TO app_runtime;

    EXEC(N'ALTER SECURITY POLICY security.TenantIsolation
        ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyMember,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyMember AFTER INSERT,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyMember AFTER UPDATE,
        ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyVisit,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyVisit AFTER INSERT,
        ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyRule,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyRule AFTER INSERT,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyRule AFTER UPDATE,
        ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyBranch,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyBranch AFTER INSERT,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON sales.LoyaltyBranch AFTER UPDATE;');

    INSERT [identity].Permission (Code, Module, Description, IsPlatformOnly)
    SELECT v.Code, v.Module, v.Description, v.IsPlatformOnly
    FROM (VALUES
        ('Sales.Loyalty.Manage', 'Sales', N'Visit rewards: add, pause and remove members; let cashiers add members at a branch', 0)
    ) AS v (Code, Module, Description, IsPlatformOnly)
    WHERE NOT EXISTS (SELECT 1 FROM [identity].Permission p WHERE p.Code = v.Code);

    -- Existing companies: the template roles Owner and Manager get it (new companies get it from
    -- their template). Branch-scoped managers keep their branch scope. Row-level security hides
    -- every company's rows from a session without a tenant, so this step reads across tenants
    -- with the bypass on, and switches it off again straight after.
    EXEC sp_set_session_context @key = N'RlsBypass', @value = 1;
    INSERT [identity].RolePermission (TenantId, RoleId, PermissionCode)
    SELECT r.TenantId, r.Id, 'Sales.Loyalty.Manage'
    FROM [identity].Role r
    JOIN tenancy.Tenant t ON t.Id = r.TenantId AND t.IsPlatform = 0
    WHERE r.Name IN (N'Owner', N'Manager')
      AND NOT EXISTS (SELECT 1 FROM [identity].RolePermission rp
                      WHERE rp.TenantId = r.TenantId AND rp.RoleId = r.Id AND rp.PermissionCode = 'Sales.Loyalty.Manage');
    PRINT CONCAT(N'Sales.Loyalty.Manage granted to ', @@ROWCOUNT, N' Owner/Manager roles.');
    EXEC sp_set_session_context @key = N'RlsBypass', @value = NULL;

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'015_Loyalty');
    COMMIT TRANSACTION;
    PRINT N'015_Loyalty applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    EXEC sp_set_session_context @key = N'RlsBypass', @value = NULL;
    THROW;
END CATCH;
