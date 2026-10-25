<#
.SYNOPSIS
  Deploys a published StockOrder.Api build to IIS. Idempotent: creates the app pool and
  site on first run, just syncs files and restarts on every run after that.

.PARAMETER SourcePath
  Folder containing the published API (the `stockorder-api` build artifact).

.PARAMETER PhysicalPath
  Where the site's files live on disk, e.g. C:\inetpub\StockOrderApi.

.PARAMETER Port
  HTTP port IIS binds to directly. In the typical topology (section 17: Cloudflare in
  front), this is the origin port behind Cloudflare, not necessarily 443.
#>
param(
    [Parameter(Mandatory)] [string]$SourcePath,
    [string]$SiteName = "StockOrderApi",
    [string]$AppPoolName = "StockOrderApi",
    [string]$PhysicalPath = "C:\inetpub\StockOrderApi",
    # 5000 and 8080 turned out to be silently blocked by the firewall in front of
    # 116.58.33.11 (ERR_CONNECTION_TIMED_OUT externally, worked fine from inside the RDP
    # session, which is what made this so confusing to diagnose) -- 5002/5001 confirmed
    # reachable (ERR_CONNECTION_REFUSED -- i.e. actually reached the server -- before
    # anything was bound there). Change these if your own network blocks different ports.
    [int]$Port = 5002,
    [string]$HealthCheckUrl = "http://localhost:5002/health/ready"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Common.ps1"

Write-Host "=== Deploying StockOrder.Api ==="
$deployStarted = Get-Date

if (-not (Test-Path $SourcePath)) {
    throw "SourcePath '$SourcePath' does not exist."
}

Ensure-AppPool -Name $AppPoolName
Stop-AppPoolIfRunning -Name $AppPoolName

# appsettings.*.json ship from the repo (no secrets in them -- section 17); never overwrite
# the environment variables a prior manual setup already configured at the machine level,
# and never touch logs/ so Serilog's rolling files survive a redeploy.
Sync-Files -Source $SourcePath -Destination $PhysicalPath -ExcludeDirs @("logs")

Ensure-Site -Name $SiteName -PhysicalPath $PhysicalPath -AppPoolName $AppPoolName -Port $Port

# Uploaded files (product pictures) go to Files__RootPath. The app pool's own identity must be
# able to write there; the setup runbook created the folder but never granted that.
$filesRoot = [Environment]::GetEnvironmentVariable("Files__RootPath", "Machine")
if ([string]::IsNullOrWhiteSpace($filesRoot)) { $filesRoot = "C:\StockOrderData\Files" }
New-Item -ItemType Directory -Path $filesRoot -Force | Out-Null
icacls $filesRoot /grant "IIS AppPool\$($AppPoolName):(OI)(CI)M" /Q | Out-Null
if ($LASTEXITCODE -ne 0) {
    $code = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    throw "Could not grant IIS AppPool\$AppPoolName write access to $filesRoot (icacls exit code $code)."
}
Write-Host "App pool '$AppPoolName' can write uploaded files to $filesRoot."
Start-SiteAndPool -SiteName $SiteName -AppPoolName $AppPoolName

try {
    Wait-ForHealthy -Url $HealthCheckUrl -TimeoutSeconds 90
} catch {
    Write-StartupDiagnostics -PhysicalPath $PhysicalPath -Since $deployStarted
    throw
}

Write-Host "=== StockOrder.Api deployed and healthy ==="
