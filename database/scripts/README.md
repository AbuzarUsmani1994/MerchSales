# Database scripts

These scripts are the **only** way the database schema changes. The API never creates or
alters tables (no EF Core migrations, no `EnsureCreated`, no Hangfire auto-schema).

## Run order (first install)

| # | Script | Run as / connected to |
|---|---|---|
| 1 | `000_CreateDatabase.sql` | sysadmin, `master` |
| 2 | `001_Foundation.sql` … `011_SeedReferenceData.sql`, in number order | sysadmin or db_owner, `StockOrderPlatform` |
| 3 | Create the app login and user (commands at the top of `010_AppDatabaseRole.sql`) | sysadmin |

From a command prompt on the server (Windows authentication):

```bat
sqlcmd -S localhost -E -b -I -i 000_CreateDatabase.sql
for %f in (0??_*.sql) do if not "%f"=="000_CreateDatabase.sql" sqlcmd -S localhost -d StockOrderPlatform -E -b -I -i "%f"
```

- `-b` stops on the first error. `-I` turns on QUOTED_IDENTIFIER (required by filtered indexes).
- In SSMS: open each file, check the database dropdown says `StockOrderPlatform`, press Execute.

## Safety rules built into every script

- **Runs once.** Each script checks `dbo.SchemaVersion` and exits if it already ran.
- **All or nothing.** Each script is one transaction; on any error nothing is applied.
- **Check what ran:** `SELECT * FROM dbo.SchemaVersion ORDER BY ScriptName;`

## Before running a script on the live server

1. Take a full backup (`BACKUP DATABASE StockOrderPlatform TO DISK = '...' WITH COMPRESSION, CHECKSUM`).
2. Run the script on a copy (restore the backup as `StockOrderPlatform_Test`) first.
3. Run it on live during the maintenance window.

## Writing new scripts (rules for developers and Claude Code)

- Next number, descriptive name: `012_RestaurantPack.sql`. Never edit a script that has run anywhere.
- Copy the header, guard, `BEGIN TRY / BEGIN TRANSACTION` and `CATCH` blocks from an existing script.
- Tenant-owned tables: `TenantId` first column, `Id uniqueidentifier` nonclustered PK,
  `ClusterKey bigint IDENTITY` with unique clustered index `(TenantId, ClusterKey)`,
  `UQ_<Table>_TenantId_Id` if other tables reference it, composite FKs `(TenantId, XId)`,
  audit columns, `RowVer rowversion`, `(TenantId, RowVer)` index if the app syncs it.
- Add every new tenant-owned table to the row-level security policy
  (template at the end of `009_RowLevelSecurity.sql`).
- `CREATE SCHEMA`, `CREATE FUNCTION`, `CREATE TRIGGER`, `CREATE VIEW` must go through `EXEC(N'...')`
  inside the transaction (they must be alone in their batch).
- Do not use `GO` after the guard: it would split the transaction.
- Reserved words are not allowed as column names (e.g. use `LineNumber`, not `LineNo`).

## Hangfire tables

Hangfire's SQL Server storage normally creates its own tables at startup. That is turned off
(`PrepareSchemaIfNecessary = false`). Phase 0 adds `012_Hangfire.sql`, generated from
Hangfire.SqlServer's install script for the pinned package version, creating schema `HangFire`
and granting `app_runtime` access to it.
