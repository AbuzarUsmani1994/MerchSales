/*
  016_RestaurantPack.sql
  Restaurant pack, part 1: tables and the kitchen screen (Build Spec section 18, Phase 2
  "Restaurant"; the spec calls it 013_RestaurantPack, but 013-015 were used by earlier
  agreed changes). Agreed with the owner, see docs/decisions.md "Tables and kitchen screen":

  - Dining areas and tables per branch. A waiter opens an order on a table, adds rounds, and
    asks for the bill; one open order per table at a time.
  - Every order sent to the kitchen gets a short daily number per branch (#1, #2, ...), counted
    per business day, so it does not restart at midnight while a shift is still open.
  - Each round is a kitchen ticket: Queued -> Preparing -> Ready -> Served (or Cancelled with
    its order). One kitchen screen per branch, no stations yet.
  - A category can be kept off the kitchen screen (e.g. drinks the counter pours itself).
  - No service charge (owner's decision). Modifiers, recipes, stations, voiding a single item
    and Bluetooth printing come in later parts.

  New schema restaurant:
    restaurant.DiningArea         areas of a branch (Main hall, Family hall, Roof)
    restaurant.DiningTable        tables in an area
    restaurant.KitchenTicket      one per round sent to the kitchen
    restaurant.KitchenTicketLine  the items of a round (names copied, so the ticket never changes)
  New table sales.DailyOrderCounter: the last short number per branch and business day.
  sales.SalesOrder gains TableId, OpenTableId (set while the order is open, so a table can't
  have two open orders), Covers, ServerUserId, OrderNo, BillRequestedAtUtc.
  catalog.Category gains SendToKitchen.
  New permissions Restaurant.Tables.Manage, Restaurant.Kitchen.Operate, Restaurant.Kitchen.Serve.

  Run after 015. Safe to run once; re-running is skipped.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'016_RestaurantPack')
BEGIN
    PRINT N'016_RestaurantPack already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    EXEC(N'CREATE SCHEMA restaurant AUTHORIZATION dbo;');

    CREATE TABLE restaurant.DiningArea
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_DiningArea_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        BranchId        uniqueidentifier NOT NULL,
        Name            nvarchar(60)     NOT NULL,
        SortOrder       int              NOT NULL CONSTRAINT DF_DiningArea_Sort DEFAULT 0,
        IsActive        bit              NOT NULL CONSTRAINT DF_DiningArea_IsActive DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_DiningArea_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_DiningArea PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_DiningArea_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_DiningArea_Name UNIQUE NONCLUSTERED (TenantId, BranchId, Name),
        CONSTRAINT FK_DiningArea_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_DiningArea ON restaurant.DiningArea (TenantId, ClusterKey);

    CREATE TABLE restaurant.DiningTable
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_DiningTable_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        BranchId        uniqueidentifier NOT NULL,
        AreaId          uniqueidentifier NOT NULL,
        Name            nvarchar(20)     NOT NULL,   -- what staff call it: "4", "T12", "Family 2"
        Seats           int              NOT NULL CONSTRAINT DF_DiningTable_Seats DEFAULT 4,
        SortOrder       int              NOT NULL CONSTRAINT DF_DiningTable_Sort DEFAULT 0,
        IsActive        bit              NOT NULL CONSTRAINT DF_DiningTable_IsActive DEFAULT 1,
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_DiningTable_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_DiningTable PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_DiningTable_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_DiningTable_Name UNIQUE NONCLUSTERED (TenantId, BranchId, Name),
        CONSTRAINT FK_DiningTable_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id),
        CONSTRAINT FK_DiningTable_Area FOREIGN KEY (TenantId, AreaId) REFERENCES restaurant.DiningArea (TenantId, Id),
        CONSTRAINT CK_DiningTable_Seats CHECK (Seats BETWEEN 1 AND 100)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_DiningTable ON restaurant.DiningTable (TenantId, ClusterKey);
    CREATE INDEX IX_DiningTable_Area ON restaurant.DiningTable (TenantId, AreaId);

    ALTER TABLE sales.SalesOrder ADD
        TableId             uniqueidentifier NULL,   -- the table the order was taken at (kept for reports)
        OpenTableId         uniqueidentifier NULL,   -- = TableId while the order is open, NULL once paid or cancelled
        Covers              int              NULL,   -- guests at the table
        ServerUserId        uniqueidentifier NULL,   -- the waiter looking after the table
        OrderNo             int              NULL,   -- short daily number for the kitchen and customers (#23)
        BillRequestedAtUtc  datetime2(3)     NULL;
    EXEC(N'ALTER TABLE sales.SalesOrder ADD
        CONSTRAINT FK_SO_Table FOREIGN KEY (TenantId, TableId) REFERENCES restaurant.DiningTable (TenantId, Id),
        CONSTRAINT FK_SO_OpenTable FOREIGN KEY (TenantId, OpenTableId) REFERENCES restaurant.DiningTable (TenantId, Id),
        CONSTRAINT FK_SO_Server FOREIGN KEY (TenantId, ServerUserId) REFERENCES [identity].[User] (TenantId, Id),
        CONSTRAINT CK_SO_Covers CHECK (Covers IS NULL OR Covers BETWEEN 1 AND 100),
        CONSTRAINT CK_SO_OpenTable CHECK (OpenTableId IS NULL OR OpenTableId = TableId);');
    -- One open order per table: a second waiter opening the same table gets DUPLICATE.
    EXEC(N'CREATE UNIQUE INDEX UX_SO_OpenTable ON sales.SalesOrder (TenantId, OpenTableId) WHERE OpenTableId IS NOT NULL;');

    CREATE TABLE sales.DailyOrderCounter
    (
        TenantId        uniqueidentifier NOT NULL,
        BranchId        uniqueidentifier NOT NULL,
        BusinessDate    date             NOT NULL,
        LastNumber      int              NOT NULL,
        CONSTRAINT PK_DailyOrderCounter PRIMARY KEY CLUSTERED (TenantId, BranchId, BusinessDate),
        CONSTRAINT FK_DailyOrderCounter_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id)
    );

    CREATE TABLE restaurant.KitchenTicket
    (
        TenantId        uniqueidentifier NOT NULL,
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_KitchenTicket_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey      bigint IDENTITY(1,1) NOT NULL,
        SalesOrderId    uniqueidentifier NOT NULL,
        BranchId        uniqueidentifier NOT NULL,
        BusinessDate    date             NOT NULL,
        OrderNo         int              NOT NULL,
        RoundNo         int              NOT NULL,
        Channel         varchar(20)      NOT NULL,   -- copied from the order: DineIn, Takeaway, ...
        TableName       nvarchar(20)     NULL,       -- copied, so renaming a table doesn't change old tickets
        Notes           nvarchar(1000)   NULL,
        Status          varchar(10)      NOT NULL CONSTRAINT DF_KitchenTicket_Status DEFAULT 'Queued',
        CreatedAtUtc    datetime2(3)     NOT NULL CONSTRAINT DF_KitchenTicket_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       uniqueidentifier NULL,
        StartedAtUtc    datetime2(3)     NULL,
        StartedBy       uniqueidentifier NULL,
        ReadyAtUtc      datetime2(3)     NULL,
        ReadyBy         uniqueidentifier NULL,
        ServedAtUtc     datetime2(3)     NULL,
        ServedBy        uniqueidentifier NULL,
        CancelledAtUtc  datetime2(3)     NULL,
        UpdatedAtUtc    datetime2(3)     NULL,
        UpdatedBy       uniqueidentifier NULL,
        RowVer          rowversion       NOT NULL,
        CONSTRAINT PK_KitchenTicket PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_KitchenTicket_TenantId_Id UNIQUE NONCLUSTERED (TenantId, Id),
        CONSTRAINT UX_KitchenTicket_Round UNIQUE NONCLUSTERED (TenantId, SalesOrderId, RoundNo),
        CONSTRAINT FK_KitchenTicket_Order FOREIGN KEY (TenantId, SalesOrderId) REFERENCES sales.SalesOrder (TenantId, Id),
        CONSTRAINT FK_KitchenTicket_Branch FOREIGN KEY (TenantId, BranchId) REFERENCES tenancy.Branch (TenantId, Id),
        CONSTRAINT CK_KitchenTicket_Status CHECK (Status IN ('Queued','Preparing','Ready','Served','Cancelled')),
        CONSTRAINT CK_KitchenTicket_Numbers CHECK (OrderNo > 0 AND RoundNo > 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_KitchenTicket ON restaurant.KitchenTicket (TenantId, ClusterKey);
    -- The board: a branch's tickets that are still in the kitchen or were finished a moment ago.
    CREATE INDEX IX_KitchenTicket_Board ON restaurant.KitchenTicket (TenantId, BranchId, Status, CreatedAtUtc);

    CREATE TABLE restaurant.KitchenTicketLine
    (
        TenantId            uniqueidentifier NOT NULL,
        Id                  uniqueidentifier NOT NULL CONSTRAINT DF_KitchenTicketLine_Id DEFAULT NEWSEQUENTIALID(),
        ClusterKey          bigint IDENTITY(1,1) NOT NULL,
        TicketId            uniqueidentifier NOT NULL,
        LineNumber          int              NOT NULL,
        SalesOrderLineId    uniqueidentifier NOT NULL,
        ProductId           uniqueidentifier NOT NULL,
        ProductName         nvarchar(200)    NOT NULL,
        Quantity            decimal(18,6)    NOT NULL,
        Notes               nvarchar(500)    NULL,
        CONSTRAINT PK_KitchenTicketLine PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_KitchenTicketLine_Ticket FOREIGN KEY (TenantId, TicketId) REFERENCES restaurant.KitchenTicket (TenantId, Id),
        CONSTRAINT FK_KitchenTicketLine_OrderLine FOREIGN KEY (TenantId, SalesOrderLineId) REFERENCES sales.SalesOrderLine (TenantId, Id),
        CONSTRAINT FK_KitchenTicketLine_Product FOREIGN KEY (TenantId, ProductId) REFERENCES catalog.Product (TenantId, Id),
        CONSTRAINT CK_KitchenTicketLine_Qty CHECK (Quantity > 0)
    );
    CREATE UNIQUE CLUSTERED INDEX CX_KitchenTicketLine ON restaurant.KitchenTicketLine (TenantId, ClusterKey);
    CREATE INDEX IX_KitchenTicketLine_Ticket ON restaurant.KitchenTicketLine (TenantId, TicketId);

    -- 0 = items of this category skip the kitchen screen (e.g. soft drinks served from the counter).
    ALTER TABLE catalog.Category ADD SendToKitchen bit NOT NULL CONSTRAINT DF_Category_SendToKitchen DEFAULT 1;

    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::restaurant TO app_runtime;
    -- Tickets are history the kitchen and reports rely on: they change status but are never deleted.
    DENY DELETE ON restaurant.KitchenTicket TO app_runtime;
    DENY UPDATE, DELETE ON restaurant.KitchenTicketLine TO app_runtime;

    EXEC(N'ALTER SECURITY POLICY security.TenantIsolation
        ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.DiningArea,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.DiningArea AFTER INSERT,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.DiningArea AFTER UPDATE,
        ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.DiningTable,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.DiningTable AFTER INSERT,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.DiningTable AFTER UPDATE,
        ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.KitchenTicket,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.KitchenTicket AFTER INSERT,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.KitchenTicket AFTER UPDATE,
        ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.KitchenTicketLine,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.KitchenTicketLine AFTER INSERT,
        ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON sales.DailyOrderCounter,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON sales.DailyOrderCounter AFTER INSERT,
        ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON sales.DailyOrderCounter AFTER UPDATE;');

    INSERT [identity].Permission (Code, Module, Description, IsPlatformOnly)
    SELECT v.Code, v.Module, v.Description, v.IsPlatformOnly
    FROM (VALUES
        ('Restaurant.Tables.Manage',   'Restaurant', N'Set up dining areas and tables', 0),
        ('Restaurant.Kitchen.Operate', 'Restaurant', N'Kitchen screen: start orders and mark them ready', 0),
        ('Restaurant.Kitchen.Serve',   'Restaurant', N'Mark ready orders as served or collected', 0)
    ) AS v (Code, Module, Description, IsPlatformOnly)
    WHERE NOT EXISTS (SELECT 1 FROM [identity].Permission p WHERE p.Code = v.Code);

    -- Existing companies (new ones get these from their template). Owners everywhere, like every
    -- other permission; Manager, Kitchen, Waiter and Cashier only in restaurant companies, where
    -- those roles mean what the template says. Branch-scoped roles keep their branch scope.
    -- Row-level security hides every company's rows from a session without a tenant, so this
    -- step reads across tenants with the bypass on, and switches it off again straight after.
    EXEC sp_set_session_context @key = N'RlsBypass', @value = 1;
    INSERT [identity].RolePermission (TenantId, RoleId, PermissionCode)
    SELECT r.TenantId, r.Id, g.PermissionCode
    FROM [identity].Role r
    JOIN tenancy.Tenant t ON t.Id = r.TenantId AND t.IsPlatform = 0
    JOIN (VALUES
        (N'Owner',   'Restaurant.Tables.Manage', 0), (N'Owner',   'Restaurant.Kitchen.Operate', 0), (N'Owner', 'Restaurant.Kitchen.Serve', 0),
        (N'Manager', 'Restaurant.Tables.Manage', 1), (N'Manager', 'Restaurant.Kitchen.Operate', 1), (N'Manager', 'Restaurant.Kitchen.Serve', 1),
        (N'Kitchen', 'Restaurant.Kitchen.Operate', 1), (N'Kitchen', 'Restaurant.Kitchen.Serve', 1),
        (N'Waiter',  'Restaurant.Kitchen.Serve', 1),
        (N'Cashier', 'Restaurant.Kitchen.Serve', 1)
    ) AS g (RoleName, PermissionCode, RestaurantOnly) ON g.RoleName = r.Name
    WHERE (g.RestaurantOnly = 0 OR t.Industry = 'Restaurant')
      AND NOT EXISTS (SELECT 1 FROM [identity].RolePermission rp
                      WHERE rp.TenantId = r.TenantId AND rp.RoleId = r.Id AND rp.PermissionCode = g.PermissionCode);
    PRINT CONCAT(N'Restaurant permissions: ', @@ROWCOUNT, N' role grants added.');

    -- Permissions are cached per security stamp: rotate the stamp of everyone in those roles so
    -- the new permissions apply at their next request instead of after the cache expires.
    UPDATE u SET SecurityStamp = NEWID()
    FROM [identity].[User] u
    WHERE EXISTS (SELECT 1 FROM [identity].UserRole ur JOIN [identity].Role r ON r.TenantId = ur.TenantId AND r.Id = ur.RoleId
                  WHERE ur.TenantId = u.TenantId AND ur.UserId = u.Id
                    AND r.Name IN (N'Owner', N'Manager', N'Kitchen', N'Waiter', N'Cashier'));
    EXEC sp_set_session_context @key = N'RlsBypass', @value = NULL;

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'016_RestaurantPack');
    COMMIT TRANSACTION;
    PRINT N'016_RestaurantPack applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    EXEC sp_set_session_context @key = N'RlsBypass', @value = NULL;
    THROW;
END CATCH;
