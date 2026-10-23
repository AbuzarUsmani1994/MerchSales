/*
  009_RowLevelSecurity.sql
  Tenant isolation enforced by SQL Server itself, as a second guard behind the API's
  EF Core query filters. Even a raw SQL query with a missing WHERE TenantId cannot
  read or write another tenant's rows.

  How the API must use it (see Build Spec, "Multi-tenancy"):
    On every connection open, before any query:
      EXEC sp_set_session_context @key = N'TenantId', @value = <tenant uniqueidentifier>, @read_only = 1;
    Only these code paths may instead set the bypass flag (never from a request value):
      - login / token refresh tenant lookup
      - the worker service (cross-tenant jobs)
      - platform-admin tenant provisioning
      EXEC sp_set_session_context @key = N'RlsBypass', @value = 1, @read_only = 1;
    With neither key set, every tenant-owned table returns zero rows.

  The policy covers every table in the module schemas that has a TenantId column,
  plus tenancy.Tenant (by its Id). Tables added by later scripts MUST be added with
  ALTER SECURITY POLICY security.TenantIsolation ADD FILTER PREDICATE ... (template at the end).
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE ScriptName = N'009_RowLevelSecurity')
BEGIN
    PRINT N'009_RowLevelSecurity already applied - skipped.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    EXEC(N'CREATE FUNCTION security.fn_TenantAccess (@TenantId uniqueidentifier)
          RETURNS TABLE
          WITH SCHEMABINDING
          AS
          RETURN
              SELECT 1 AS AccessGranted
              WHERE @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS uniqueidentifier)
                 OR CAST(SESSION_CONTEXT(N''RlsBypass'') AS bit) = 1;');

    DECLARE @sql nvarchar(max), @tableCount int;

    ;WITH target AS
    (
        SELECT QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) AS TableName,
               QUOTENAME(c.name) AS PredicateColumn
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = t.object_id
        WHERE s.name IN (N'tenancy', N'identity', N'catalog', N'inventory', N'purchasing',
                         N'sales', N'finance', N'platform', N'sync')
          AND (   (c.name = N'TenantId')
               OR (s.name = N'tenancy' AND t.name = N'Tenant' AND c.name = N'Id'))
    )
    SELECT @tableCount = COUNT(*),
           @sql = N'CREATE SECURITY POLICY security.TenantIsolation' + NCHAR(10)
                + STRING_AGG(CAST(
                      N'ADD FILTER PREDICATE security.fn_TenantAccess(' + PredicateColumn + N') ON ' + TableName + N',' + NCHAR(10)
                    + N'ADD BLOCK PREDICATE security.fn_TenantAccess(' + PredicateColumn + N') ON ' + TableName + N' AFTER INSERT,' + NCHAR(10)
                    + N'ADD BLOCK PREDICATE security.fn_TenantAccess(' + PredicateColumn + N') ON ' + TableName + N' AFTER UPDATE'
                    AS nvarchar(max)), N',' + NCHAR(10))
                + NCHAR(10) + N'WITH (STATE = ON, SCHEMABINDING = ON);'
    FROM target;

    EXEC sys.sp_executesql @sql;
    PRINT CONCAT(N'Row-level security policy created on ', @tableCount, N' tables.');

    INSERT dbo.SchemaVersion (ScriptName) VALUES (N'009_RowLevelSecurity');
    COMMIT TRANSACTION;
    PRINT N'009_RowLevelSecurity applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

/*
  Template for tables created by later scripts (put it inside that script's transaction):

  ALTER SECURITY POLICY security.TenantIsolation
      ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.DiningTable,
      ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.DiningTable AFTER INSERT,
      ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON restaurant.DiningTable AFTER UPDATE;

  Verification query (run as the app login with a tenant set; expect 0 rows from other tenants):
  SELECT TenantId, COUNT(*) FROM catalog.Product GROUP BY TenantId;
*/
