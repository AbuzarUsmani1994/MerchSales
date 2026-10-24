<#
.SYNOPSIS
  One-time setup of the self-hosted GitHub Actions runner on this server (section 17.1,
  docs/deploy-runbook.md step 6). Run as Administrator, on 116.58.33.11 itself.

.PARAMETER Token
  The registration token from GitHub: Settings -> Actions -> Runners -> New self-hosted
  runner. Expires ~1 hour after being issued -- if this script fails with an auth error,
  get a fresh token and re-run.

.EXAMPLE
  .\Install-Runner.ps1 -Token "AQTxxxxxxxxxxxxxxxxxxxxxxxx"
#>
param(
    [Parameter(Mandatory)] [string]$Token,
    [string]$RepoUrl = "https://github.com/AbuzarUsmani1994/MerchSales",
    [string]$RunnerVersion = "2.338.0",
    [string]$InstallPath = "C:\actions-runner",
    [string]$RunnerName = "stockorder-prod-01",
    [string]$Labels = "self-hosted,windows,stockorder-prod"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}
Set-Location $InstallPath

$zipName = "actions-runner-win-x64-$RunnerVersion.zip"
if (-not (Test-Path $zipName)) {
    Write-Host "Downloading runner $RunnerVersion..."
    Invoke-WebRequest -Uri "https://github.com/actions/runner/releases/download/v$RunnerVersion/$zipName" -OutFile $zipName
}

if (-not (Test-Path ".\config.cmd") -or -not (Test-Path ".\bin\Runner.Listener.exe")) {
    Write-Host "Extracting..."
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory("$InstallPath\$zipName", $InstallPath)
}

Write-Host "Configuring runner and installing it as a Windows Service (unattended, labels: $Labels)..."
# --unattended skips the interactive prompts; --replace lets you re-run this safely if a
# runner with this name was already registered (e.g. after a token expired mid-setup).
# --runasservice configures AND installs+starts the Windows Service in one step. Current
# runner releases (2.3xx) don't ship a standalone svc.cmd any more -- the install logic
# moved into config.cmd itself. If you see ".\svc.cmd not recognized", that's why; this
# flag is the fix (discovered the hard way deploying to 116.58.33.11 on 2026-10-24).
.\config.cmd --url $RepoUrl --token $Token --name $RunnerName --labels $Labels --unattended --replace --runasservice

Write-Host ""
Write-Host "=== Done. Check https://github.com/AbuzarUsmani1994/MerchSales/settings/actions/runners -- '$RunnerName' should show Idle (green). ==="
