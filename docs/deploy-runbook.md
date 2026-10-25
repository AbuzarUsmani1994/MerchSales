# First-time deployment runbook (self-hosted runner)

One-time setup to connect your server to GitHub Actions so every push to `main` deploys
automatically. You run these yourself on 116.58.33.11 (RDP in, open PowerShell as
Administrator) — nothing here needs you to share the server's password with anyone.

**Also needed:** the `API_BASE_URL` repository variable (Settings → Secrets and variables
→ Actions → Variables) — same one `docs/mobile-build.md` already describes for the mobile
app — e.g. `http://116.58.33.11:5000`. `web-admin.yml`'s deploy step reads it to write
`config.json` so the Angular site knows where the API is.

## 1. Confirm prerequisites

```powershell
# IIS + URL Rewrite module (needed for the web-admin SPA fallback rule)
Get-WindowsFeature Web-Server | Select-Object InstallState
Get-Module -ListAvailable WebAdministration

# .NET Hosting Bundle (lets IIS run the API in-process)
Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Updates\.NET Core*" -ErrorAction SilentlyContinue
dotnet --list-runtimes | Select-String "Microsoft.AspNetCore.App 10"
```

If the URL Rewrite module isn't installed, grab it from
<https://www.iis.net/downloads/microsoft/url-rewrite> (small MSI, no reboot needed).

## 2. Verify the database

```powershell
sqlcmd -S localhost -d StockOrderPlatform -E -C -Q "SELECT ScriptName FROM dbo.SchemaVersion ORDER BY ScriptName;"
```

You said scripts were already run there — but `012_Hangfire.sql` and
`013_SelfServicePermission.sql` were added *after* that, during this session. Pull the
latest `database/scripts/` from the repo and run whichever of these two are missing from
the list above, in order:

```bat
sqlcmd -S localhost -d StockOrderPlatform -E -b -I -i "012_Hangfire.sql"
sqlcmd -S localhost -d StockOrderPlatform -E -b -I -i "013_SelfServicePermission.sql"
```

### Create the app_runtime SQL login (Mixed Mode is enabled there)

```sql
-- Run in SSMS or sqlcmd, connected as sysadmin, against [master] then [StockOrderPlatform]
USE [master];
CREATE LOGIN [sop_app] WITH PASSWORD = N'<pick a long random password>', CHECK_POLICY = ON;
USE [StockOrderPlatform];
CREATE USER [sop_app] FOR LOGIN [sop_app];
ALTER ROLE [app_runtime] ADD MEMBER [sop_app];
```

Save that password — you'll put it in an environment variable in step 5, never in a file
that gets committed.

## 3. Generate a signing certificate (one-time, persists across restarts)

The ephemeral dev certificate (used automatically when nothing is configured) regenerates
every restart, which would invalidate every outstanding token on every deploy. Generate a
real one:

```powershell
$cert = New-SelfSignedCertificate -Subject "CN=StockOrder" -KeyAlgorithm RSA -KeyLength 2048 `
    -CertStoreLocation "Cert:\LocalMachine\My" -NotAfter (Get-Date).AddYears(5) -KeyExportPolicy Exportable

$certPassword = ConvertTo-SecureString -String "<pick a password for the pfx file>" -Force -AsPlainText
New-Item -ItemType Directory -Path "C:\StockOrderData\Certs" -Force | Out-Null
Export-PfxCertificate -Cert $cert -FilePath "C:\StockOrderData\Certs\signing.pfx" -Password $certPassword
```

## 4. Create the file storage folder

```powershell
New-Item -ItemType Directory -Path "C:\StockOrderData\Files" -Force
# The API's app pool writes uploaded files (product pictures) here. Deploy-Api.ps1 also does this on every deploy.
icacls "C:\StockOrderData\Files" /grant "IIS AppPool\StockOrderApi:(OI)(CI)M"
```

## 5. Set machine-level environment variables

System Properties → Advanced → Environment Variables → System variables → New (or
PowerShell as Administrator, then **restart the machine** or at least IIS/the runner
service so they're picked up):

```powershell
[Environment]::SetEnvironmentVariable("ConnectionStrings__Main",
  "Server=localhost;Database=StockOrderPlatform;User Id=sop_app;Password=<the sop_app password>;Encrypt=True;TrustServerCertificate=True;Application Name=StockOrder.Api",
  "Machine")
[Environment]::SetEnvironmentVariable("Auth__SigningCertificatePath", "C:\StockOrderData\Certs\signing.pfx", "Machine")
[Environment]::SetEnvironmentVariable("Auth__SigningCertificatePassword", "<the pfx password>", "Machine")
[Environment]::SetEnvironmentVariable("Files__RootPath", "C:\StockOrderData\Files", "Machine")
[Environment]::SetEnvironmentVariable("AdminSite__Origin", "http://116.58.33.11:8080", "Machine")
[Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production", "Machine")
```

`AdminSite__Origin` is CORS (section 17): set it to wherever the Angular site will actually
be served from — change it once you have a real domain/Cloudflare address in front.

## 6. Install the self-hosted GitHub Actions runner

Use `deploy/Install-Runner.ps1` (download just that one file, no `git clone` needed):

```powershell
mkdir C:\actions-runner; cd C:\actions-runner
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/AbuzarUsmani1994/MerchSales/main/deploy/Install-Runner.ps1" -OutFile "Install-Runner.ps1"
.\Install-Runner.ps1 -Token "<fresh token from GitHub, see below>" -InstallPath "C:\actions-runner"
```

1. Get the token from: repo → **Settings** → **Actions** → **Runners** → **New self-hosted
   runner** → Windows → x64 → copy the short value after `--token` in the "Configure" box.
   It expires fast (~1 hour) — grab it right before running the script.
2. The script downloads the runner, registers it against this repo with labels
   `self-hosted,windows,stockorder-prod` (matches the workflows' `runs-on:`), and installs
   it as a Windows Service **in one step** via `config.cmd --runasservice` — current runner
   releases (2.3xx) no longer ship a standalone `svc.cmd`, so don't follow older
   instructions that reference it separately.
3. Confirm it shows **Idle** (green) on the GitHub repo's Runners page
   (`…/settings/actions/runners?tab=self-hosted`).

**If this server already runs a different self-hosted runner for another project** (check
first with `Get-Service -Name "actions.runner.*"`), install this one into a different
folder (e.g. `C:\actions-runner-stockorder`) — each runner needs its own folder, and the
Windows Service name is derived automatically from the repo + runner name, so they won't
collide as long as the folders are separate.

The runner service runs as `NT AUTHORITY\NETWORK SERVICE` by default — give that account
rights to manage IIS (`IIS_IUSRS` / local Administrators) and to start/stop/create the
`StockOrderWorker` service, since the deploy scripts do both.

## 7. First deploy

Push to `main` (or re-run the `backend` and `web-admin` workflows manually from the
Actions tab). Each workflow's `deploy` job will:

- **backend.yml**: run `deploy/Deploy-Api.ps1` (creates the `StockOrderApi` IIS site/app
  pool on port 5000 the first time, syncs files, waits for `/health/ready`) and
  `deploy/Deploy-Worker.ps1` (creates the `StockOrderWorker` Windows service the first
  time, syncs files, restarts it).
- **web-admin.yml**: run `deploy/Deploy-WebAdmin.ps1` (creates the `StockOrderAdmin` IIS
  site on port 8080 the first time, writes `config.json` pointing at the API, writes the
  SPA rewrite `web.config`).

Watch the run under the **Actions** tab — the deploy job's log shows exactly which step
it's on. If `Deploy-Api.ps1` fails at the health check, SSH/RDP in and check
`C:\inetpub\StockOrderApi\logs\` (Serilog rolling files) for the real error.

## 8. Bootstrap the platform tenant (once)

RDP in and run, from the deployed API folder:

```powershell
cd C:\inetpub\StockOrderApi
.\StockOrder.Api.exe bootstrap-platform --admin youradminusername
```

This prints a one-time password. Sign in at `http://116.58.33.11:8080/` with tenant code
`PLATFORM`, change the password immediately, then use the **Platform tenants** screen to
provision your real tenants.

## 9. One last thing: Cloudflare / real domain (optional, later)

Right now this is reachable at `http://116.58.33.11:5000` (API) and
`http://116.58.33.11:8080` (admin site) — fine for testing, not for real users (no HTTPS).
When you're ready: point a domain at the server, put Cloudflare in front (per section 17),
update `AdminSite__Origin` to the real HTTPS admin URL, and update the `API_BASE_URL`
repository variable (used by `mobile.yml`) and `web-admin`'s `config.json` write-up in
`Deploy-WebAdmin.ps1`'s `-ApiBaseUrl` parameter to match.
