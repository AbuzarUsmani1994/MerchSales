# Operations runbook

## Install (first time)

### Prerequisites on the server

- Windows Server with IIS, **ASP.NET Core Hosting Bundle** for .NET 10 installed
  (enables IIS to run the Kestrel-backed in-process module).
- SQL Server 2019 or later, reachable from the server (can be the same machine).
- IIS features: Windows Authentication not required; enable **WebSockets** (needed for
  SignalR in Phase 2).

### Steps

1. **Database.** Copy `database/scripts/` to the SQL Server machine. Follow
   `database/scripts/README.md` exactly, in order: `000_CreateDatabase.sql` as sysadmin
   against `master`, then every other script in numeric order against `StockOrderPlatform`,
   then the `CREATE LOGIN` / `CREATE USER` commands at the top of `010_AppDatabaseRole.sql`.
2. **Signing certificate.** Generate or obtain an RSA certificate for signing access
   tokens (a self-signed one is fine — it's never shown to end users, only used by the API
   to validate its own tokens). Export it as a `.pfx` with a password, and note the path.
3. **File storage folder.** Create a folder outside the web root for attachments, e.g.
   `C:\StockOrderData\Files`. Grant the IIS app pool identity read/write access.
4. **Environment variables.** Set these as machine-level environment variables (System
   Properties → Environment Variables), then restart IIS so it picks them up:
   - `ConnectionStrings__Main` — e.g.
     `Server=localhost;Database=StockOrderPlatform;User Id=sop_app;Password=...;Encrypt=True;TrustServerCertificate=False;Application Name=StockOrder.Api`
   - `Auth__SigningCertificatePath` and `Auth__SigningCertificatePassword`
   - `Files__RootPath` — the folder from step 3
   - `Seq__Url` — optional; leave unset/empty to disable Seq logging
   - `Firebase__CredentialsPath` — optional; leave unset to disable push (Phase 1+)
5. **Deploy the API.** Download the `stockorder-api` artifact from the latest successful
   `backend` GitHub Actions run, extract it to a folder (e.g.
   `C:\inetpub\StockOrderApi`), and create an IIS site pointing at it. App pool: "No Managed
   Code", **Start Mode: AlwaysRunning**, enable **Preload**, set **Idle Timeout to 0**.
6. **Deploy the Worker.** Download the `stockorder-worker` artifact, extract it (e.g. to
   `C:\Services\StockOrderWorker`), then from an elevated prompt:
   ```bat
   sc create StockOrderWorker binPath= "C:\Services\StockOrderWorker\StockOrder.Worker.exe"
   sc start StockOrderWorker
   ```
7. **Deploy the admin site.** Download the `web-admin-dist` artifact from the latest
   `web-admin` run, extract it to an IIS site's folder, edit `config.json` in that folder to
   point `apiBaseUrl` at the API site's address, and add a URL Rewrite rule sending unknown
   routes to `index.html` (so Angular's client-side routing works on refresh/deep links).
8. **Bootstrap the platform tenant.** From the API's install folder:
   ```bat
   StockOrder.Api.exe bootstrap-platform --admin youradminusername
   ```
   This prints a one-time password — save it, then sign in at the admin site with tenant
   code `PLATFORM` and change the password immediately.
9. **Smoke test.** Visit `https://your-api-host/health/ready` — should return `Healthy`.
   Sign in to the admin site, create a test tenant, sign in as that tenant's owner.

## Backup plan

- **Full backup**: daily, e.g. 2 AM, `BACKUP DATABASE StockOrderPlatform TO DISK = '...'
  WITH COMPRESSION, CHECKSUM`.
- **Differential backup**: hourly.
- **Transaction log backup**: every 15 minutes (required — the database is in FULL
  recovery mode per `000_CreateDatabase.sql`, so logs must be backed up regularly or the log
  file grows unbounded).
- Copy backups off the machine (a second disk, network share, or cloud storage) — a backup
  that lives only on the machine it protects doesn't survive that machine failing.

## Restore drill

Run this against a *copy* of the server, not the live one, at least once before you need it
for real:

1. Restore the full backup, then each differential since, then each log backup since, in
   order, using `WITH NORECOVERY` until the last one, then `WITH RECOVERY`.
2. Point a test API instance at the restored database and confirm `/health/ready` is
   healthy and you can sign in.

## Upgrade procedure

1. **Backup first.** Full backup, verified restorable.
2. **Run new database scripts.** Copy any new numbered scripts to the SQL Server machine
   and run them in order — each one checks `dbo.SchemaVersion` and is safe to run even if
   some of the batch was already applied by a previous partial run.
3. **Deploy the new API and Worker** artifacts (stop the Worker service first, then the IIS
   app pool, replace files, restart both — the API refuses to start if a script it expects
   is missing, so if deployment order is wrong you'll see it immediately in `/health/ready`
   and the Windows Event Log / Serilog file).
4. **Deploy the new admin site** build.
5. **Smoke test**: health checks, sign-in, one read and one write action.
