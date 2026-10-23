/*
  000_CreateDatabase.sql
  Run ONCE, connected to [master], by a sysadmin.
  Creates the application database and sets the options the application depends on.
  Change the database name below if you use a different one; keep it consistent with
  the API connection string.

  Safe to re-run: it does nothing if the database already exists (except re-applying options).
*/
SET NOCOUNT ON;

IF DB_ID(N'StockOrderPlatform') IS NULL
BEGIN
    CREATE DATABASE [StockOrderPlatform];
    PRINT N'Database StockOrderPlatform created.';
END
ELSE
    PRINT N'Database StockOrderPlatform already exists.';
GO

-- Read Committed Snapshot Isolation: readers never block writers (important for
-- reports and sync pulls running while orders are posted).
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'StockOrderPlatform' AND is_read_committed_snapshot_on = 0)
BEGIN
    ALTER DATABASE [StockOrderPlatform] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
    PRINT N'READ_COMMITTED_SNAPSHOT enabled.';
END
GO

ALTER DATABASE [StockOrderPlatform] SET ALLOW_SNAPSHOT_ISOLATION ON;
ALTER DATABASE [StockOrderPlatform] SET RECOVERY FULL;   -- required for 15-minute log backups
GO
