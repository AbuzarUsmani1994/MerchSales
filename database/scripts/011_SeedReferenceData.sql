/*
  011_SeedReferenceData.sql
  Global reference data: the permission catalogue and the initial app version policy.
  Tenant data (roles, units, document types, number series...) is NOT seeded here:
  the API's tenant provisioning applies an industry template (JSON) when a tenant is created.

  New permissions in later releases: add a new numbered script that inserts only the new codes.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'011_SeedReferenceData')
BEGIN
    PRINT N'011_SeedReferenceData already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    INSERT [identity].Permission (Code, Module, Description, IsPlatformOnly)
    SELECT v.Code, v.Module, v.Description, v.IsPlatformOnly
    FROM (VALUES
        -- Platform (operator only)
        ('Platform.Tenants.Manage',        'Platform',   N'Create, suspend and configure tenants', 1),
        ('Platform.AppVersion.Manage',     'Platform',   N'Set minimum and latest mobile app versions', 1),

        -- Administration
        ('Users.View',                     'Identity',   N'View users', 0),
        ('Users.Manage',                   'Identity',   N'Create users, reset passwords, assign roles', 0),
        ('Roles.Manage',                   'Identity',   N'Create roles and change their permissions', 0),
        ('Devices.Manage',                 'Identity',   N'View, revoke and wipe mobile devices', 0),
        ('Settings.Manage',                'Settings',   N'Features, number series, document types, custom fields, terminology', 0),
        ('Branches.Manage',                'Settings',   N'Branches and warehouses', 0),
        ('Audit.View',                     'Settings',   N'View the audit log', 0),

        -- Catalog and pricing
        ('Catalog.View',                   'Catalog',    N'View products, categories, units', 0),
        ('Catalog.Manage',                 'Catalog',    N'Create and edit products, categories, units, barcodes', 0),
        ('Catalog.Import',                 'Catalog',    N'Import catalog data from Excel', 0),
        ('Pricing.View',                   'Catalog',    N'View price lists', 0),
        ('Pricing.Manage',                 'Catalog',    N'Edit price lists and tax codes', 0),

        -- Inventory
        ('Inventory.View',                 'Inventory',  N'View stock levels, batches and movements', 0),
        ('Inventory.Cost.View',            'Inventory',  N'View unit costs and stock value', 0),
        ('Inventory.Document.Create',      'Inventory',  N'Create stock documents (opening stock, adjustments, wastage)', 0),
        ('Inventory.Document.Post',        'Inventory',  N'Post stock documents', 0),
        ('Inventory.Document.Approve',     'Inventory',  N'Approve stock documents above threshold', 0),
        ('Inventory.Transfer.Create',      'Inventory',  N'Create and dispatch transfers', 0),
        ('Inventory.Transfer.Receive',     'Inventory',  N'Receive transfers at destination', 0),
        ('Inventory.Count.Perform',        'Inventory',  N'Enter stock count quantities', 0),
        ('Inventory.Count.Post',           'Inventory',  N'Review and post stock counts', 0),
        ('Inventory.Batch.Manage',         'Inventory',  N'Block, unblock and recall batches', 0),

        -- Purchasing
        ('Purchasing.View',                'Purchasing', N'View suppliers, purchase orders and receipts', 0),
        ('Purchasing.Supplier.Manage',     'Purchasing', N'Create and edit suppliers', 0),
        ('Purchasing.Order.Create',        'Purchasing', N'Create purchase orders', 0),
        ('Purchasing.Order.Approve',       'Purchasing', N'Approve purchase orders', 0),
        ('Purchasing.Receipt.Post',        'Purchasing', N'Post goods receipts (stock entry from suppliers)', 0),
        ('Purchasing.Return.Post',         'Purchasing', N'Post purchase returns', 0),

        -- Sales
        ('Sales.View',                     'Sales',      N'View customers, orders and invoices', 0),
        ('Sales.Customer.Manage',          'Sales',      N'Create and edit customers', 0),
        ('Sales.Order.Create',             'Sales',      N'Create and submit sales orders', 0),
        ('Sales.Order.Confirm',            'Sales',      N'Confirm orders (reserve stock)', 0),
        ('Sales.Order.Fulfil',             'Sales',      N'Pick, dispatch and deliver orders', 0),
        ('Sales.Order.Cancel',             'Sales',      N'Cancel orders before dispatch', 0),
        ('Sales.Price.Override',           'Sales',      N'Change a unit price on an order line', 0),
        ('Sales.Discount.Apply',           'Sales',      N'Apply discounts above the role limit', 0),
        ('Sales.Credit.Override',          'Sales',      N'Release an order held for credit limit', 0),
        ('Sales.Invoice.Post',             'Sales',      N'Generate invoices', 0),
        ('Sales.Invoice.Cancel',           'Sales',      N'Cancel invoices', 0),
        ('Sales.Return.Post',              'Sales',      N'Post sales returns', 0),

        -- Finance
        ('Finance.Payment.Receive',        'Finance',    N'Receive customer payments', 0),
        ('Finance.Payment.Make',           'Finance',    N'Record payments to suppliers and refunds', 0),
        ('Finance.CashSession.Operate',    'Finance',    N'Open a cash session and take payments', 0),
        ('Finance.CashSession.Close',      'Finance',    N'Close cash sessions and record counted cash', 0),
        ('Finance.Ledger.View',            'Finance',    N'View customer and supplier balances', 0),
        ('Finance.Period.Close',           'Finance',    N'Close accounting periods', 0),

        -- Reports
        ('Reports.Stock.View',             'Reports',    N'Stock reports', 0),
        ('Reports.Sales.View',             'Reports',    N'Sales reports', 0),
        ('Reports.Purchasing.View',        'Reports',    N'Purchasing reports', 0),
        ('Reports.Financial.View',         'Reports',    N'Margin, receivables and payables reports', 0),
        ('Reports.Export',                 'Reports',    N'Export reports to Excel and PDF', 0),

        -- Mobile
        ('Mobile.Use',                     'Sync',       N'Sign in on the mobile app and sync', 0)
    ) AS v (Code, Module, Description, IsPlatformOnly)
    WHERE NOT EXISTS (SELECT 1 FROM [identity].Permission p WHERE p.Code = v.Code);

    INSERT sync.AppVersionPolicy (Platform, MinVersion, LatestVersion)
    SELECT v.Platform, '1.0.0', '1.0.0'
    FROM (VALUES ('Android'), ('iOS')) AS v (Platform)
    WHERE NOT EXISTS (SELECT 1 FROM sync.AppVersionPolicy a WHERE a.Platform = v.Platform);

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'011_SeedReferenceData');
    COMMIT TRANSACTION;
    PRINT N'011_SeedReferenceData applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
