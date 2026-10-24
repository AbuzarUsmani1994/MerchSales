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
    [int]$Port = 5000,
    [string]$HealthCheckUrl = "http://localhost:5000/health/ready"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Common.ps1"

Write-Host "=== Deploying StockOrder.Api ==="

if (-not (Test-Path $SourcePath)) {
    throw "SourcePath '$SourcePath' does not exist."
}

Ensure-AppPool -Name $AppPoolName
Stop-AppPoolIfRunning -Name $AppPoolName

# appsettings.*.json ship from the repo (no secrets in them — section 17); never overwrite
# the environment variables a prior manual setup already configured at the machine level,
# and never touch logs/ so Serilog's rolling files survive a redeploy.
Sync-Files -Source $SourcePath -Destination $PhysicalPath -ExcludeDirs @("logs")

Ensure-Site -Name $SiteName -PhysicalPath $PhysicalPath -AppPoolName $AppPoolName -Port $Port
Start-SiteAndPool -SiteName $SiteName -AppPoolName $AppPoolName

Wait-ForHealthy -Url $HealthCheckUrl -TimeoutSeconds 90

Write-Host "=== StockOrder.Api deployed and healthy ==="
