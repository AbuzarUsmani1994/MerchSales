<#
.SYNOPSIS
  Deploys the Angular back-office build as static files to IIS, writes a fresh
  config.json pointing at this server's own API, and ensures the SPA URL Rewrite rule
  exists (section 13: "unknown routes to index.html"). Requires the IIS URL Rewrite
  module to already be installed (see docs/deploy-runbook.md) -- that's a separate IIS
  extension this script cannot install itself.
#>
param(
    [Parameter(Mandatory)] [string]$SourcePath,
    [Parameter(Mandatory)] [string]$ApiBaseUrl,
    [string]$SiteName = "StockOrderAdmin",
    [string]$AppPoolName = "StockOrderAdmin",
    [string]$PhysicalPath = "C:\inetpub\StockOrderAdmin",
    [int]$Port = 8080
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Common.ps1"

Write-Host "=== Deploying StockOrder web-admin ==="

if (-not (Test-Path $SourcePath)) {
    throw "SourcePath '$SourcePath' does not exist."
}

Ensure-AppPool -Name $AppPoolName
Stop-AppPoolIfRunning -Name $AppPoolName

# config.json and web.config are written fresh below, after the copy -- excluded here so
# robocopy's /MIR doesn't delete them as "extra" files not present in the build output.
Sync-Files -Source $SourcePath -Destination $PhysicalPath -ExcludeFiles @("config.json", "web.config")

# Runtime config (section 13): never baked into the build, always written fresh per
# environment so the same artifact can deploy to dev/staging/prod.
@{ apiBaseUrl = $ApiBaseUrl } | ConvertTo-Json | Set-Content -Path (Join-Path $PhysicalPath "config.json") -Encoding utf8

$webConfigPath = Join-Path $PhysicalPath "web.config"
if (-not (Test-Path $webConfigPath)) {
    Write-Host "Writing web.config (SPA fallback rewrite rule)..."
    @'
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="Angular routes" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>
    <staticContent>
      <remove fileExtension=".json" />
      <mimeMap fileExtension=".json" mimeType="application/json" />
    </staticContent>
  </system.webServer>
</configuration>
'@ | Set-Content -Path $webConfigPath -Encoding utf8
}

Ensure-Site -Name $SiteName -PhysicalPath $PhysicalPath -AppPoolName $AppPoolName -Port $Port
Start-SiteAndPool -SiteName $SiteName -AppPoolName $AppPoolName

Write-Host "=== StockOrder web-admin deployed (http://localhost:$Port/) ==="
