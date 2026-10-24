<#
.SYNOPSIS
  Deploys a published StockOrder.Worker build as a Windows Service. Idempotent: creates
  the service on first run, stops/replaces files/starts on every run after that.
#>
param(
    [Parameter(Mandatory)] [string]$SourcePath,
    [string]$ServiceName = "StockOrderWorker",
    [string]$PhysicalPath = "C:\Services\StockOrderWorker"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Common.ps1"

Write-Host "=== Deploying StockOrder.Worker ==="

if (-not (Test-Path $SourcePath)) {
    throw "SourcePath '$SourcePath' does not exist."
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -eq "Running") {
    Write-Host "Stopping service '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force
    # Wait for the process to actually exit so its binaries aren't locked during the copy.
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Service -Name $ServiceName).Status -ne "Stopped" -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
    }
}

Sync-Files -Source $SourcePath -Destination $PhysicalPath -ExcludeDirs @("logs")

$exePath = Join-Path $PhysicalPath "StockOrder.Worker.exe"
if (-not (Test-Path $exePath)) {
    throw "Expected $exePath after copy -- check the published artifact contains the Worker executable."
}

if (-not $existingService) {
    Write-Host "Creating service '$ServiceName'..."
    New-Service -Name $ServiceName -BinaryPathName $exePath -DisplayName "StockOrder Worker" `
        -StartupType Automatic -Description "StockOrder outbox dispatcher + Hangfire server (section 15)." | Out-Null
} else {
    # BinaryPathName can change between versions of .NET/publish settings -- keep it in sync.
    sc.exe config $ServiceName binPath= "`"$exePath`"" | Out-Null
    # Defensive: don't let sc.exe's exit code become this script's own exit code (see the
    # robocopy/$LASTEXITCODE note in Common.ps1's Sync-Files -- same class of bug).
    $LASTEXITCODE = 0
}

Start-Service -Name $ServiceName
Start-Sleep -Seconds 5

$status = (Get-Service -Name $ServiceName).Status
if ($status -ne "Running") {
    throw "Service '$ServiceName' did not reach Running state (currently: $status). Check the Windows Event Log and logs/ folder before retrying."
}

Write-Host "=== StockOrder.Worker deployed and running ==="
