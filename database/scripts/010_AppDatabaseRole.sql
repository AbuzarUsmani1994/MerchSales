/*
  010_AppDatabaseRole.sql
  Creates the database role the API and worker use at runtime, with least privilege:
  - read/write on module schemas
  - NO update/delete on append-only tables (ledger, party ledger, audit log)
  - NO DDL rights (schema changes only through these numbered scripts)

  After running it, create the login and user ONCE (as sysadmin), with your own password:

    USE [master];
    CREATE LOGIN [sop_app] WITH PASSWORD = N'<long random password>', CHECK_POLICY = ON;
    USE [StockOrderPlatform];
    CREATE USER [sop_app] FOR LOGIN [sop_app];
    ALTER ROLE [app_runtime] ADD MEMBER [sop_app];

  (If the API app pool runs as a Windows/virtual account, map that account instead and
  use Integrated Security in the connection string - no password to store.)
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'010_AppDatabaseRole')
BEGIN
    PRINT N'010_AppDatabaseRole already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF DATABASE_PRINCIPAL_ID(N'app_runtime') IS NULL
        EXEC(N'CREATE ROLE app_runtime AUTHORIZATION dbo');

    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::tenancy    TO app_runtime;
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[identity] TO app_runtime;
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::catalog    TO app_runtime;
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::inventory  TO app_runtime;
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::purchasing TO app_runtime;
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::sales      TO app_runtime;
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::finance    TO app_runtime;
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::platform   TO app_runtime;
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::sync       TO app_runtime;
    GRANT SELECT ON dbo.SchemaVersion TO app_runtime;   -- the API checks the schema version at startup

    -- Append-only tables: insert and read only
    DENY UPDATE, DELETE ON inventory.StockLedgerEntry TO app_runtime;
    DENY UPDATE, DELETE ON finance.PartyLedgerEntry   TO app_runtime;
    DENY UPDATE, DELETE ON platform.AuditLog          TO app_runtime;

    -- Global reference data is maintained by scripts only
    DENY INSERT, UPDATE, DELETE ON [identity].Permission TO app_runtime;

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'010_AppDatabaseRole');
    COMMIT TRANSACTION;
    PRINT N'010_AppDatabaseRole applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
