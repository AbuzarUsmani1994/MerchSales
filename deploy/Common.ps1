# Shared helpers for the deploy scripts. Dot-sourced, not run directly.
# All functions are idempotent: safe to run on every deploy, not just the first one.

function Ensure-AppPool {
    param(
        [Parameter(Mandatory)] [string]$Name
    )

    Import-Module WebAdministration -ErrorAction Stop

    if (-not (Test-Path "IIS:\AppPools\$Name")) {
        Write-Host "Creating app pool '$Name'..."
        New-WebAppPool -Name $Name | Out-Null
    }

    Set-ItemProperty "IIS:\AppPools\$Name" -Name managedRuntimeVersion -Value ""
    Set-ItemProperty "IIS:\AppPools\$Name" -Name startMode -Value "AlwaysRunning"
    Set-ItemProperty "IIS:\AppPools\$Name" -Name processModel.idleTimeout -Value "00:00:00"
}

function Stop-AppPoolIfRunning {
    param([Parameter(Mandatory)] [string]$Name)

    Import-Module WebAdministration -ErrorAction Stop
    if (Test-Path "IIS:\AppPools\$Name") {
        $state = Get-WebAppPoolState -Name $Name
        if ($state.Value -eq "Started") {
            Write-Host "Stopping app pool '$Name'..."
            Stop-WebAppPool -Name $Name
            # File locks release asynchronously — give IIS a moment.
            Start-Sleep -Seconds 3
        }
    }
}

function Ensure-Site {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$PhysicalPath,
        [Parameter(Mandatory)] [string]$AppPoolName,
        [Parameter(Mandatory)] [int]$Port,
        [string]$HostHeader = ""
    )

    Import-Module WebAdministration -ErrorAction Stop

    if (-not (Test-Path $PhysicalPath)) {
        New-Item -ItemType Directory -Path $PhysicalPath -Force | Out-Null
    }

    if (-not (Test-Path "IIS:\Sites\$Name")) {
        Write-Host "Creating site '$Name' on port $Port..."
        $binding = if ($HostHeader) { "*:$($Port):$HostHeader" } else { "*:$($Port):" }
        New-Website -Name $Name -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName `
            -Port $Port -HostHeader $HostHeader -Force | Out-Null
    } else {
        Set-ItemProperty "IIS:\Sites\$Name" -Name applicationPool -Value $AppPoolName
        Set-ItemProperty "IIS:\Sites\$Name" -Name physicalPath -Value $PhysicalPath
    }
}

function Start-SiteAndPool {
    param(
        [Parameter(Mandatory)] [string]$SiteName,
        [Parameter(Mandatory)] [string]$AppPoolName
    )

    Import-Module WebAdministration -ErrorAction Stop
    Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Website -Name $SiteName -ErrorAction SilentlyContinue
}

function Sync-Files {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination,
        [string[]]$ExcludeFiles = @(),
        [string[]]$ExcludeDirs = @()
    )

    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }

    $robocopyArgs = @($Source, $Destination, "/MIR", "/NFL", "/NDL", "/NJH", "/NJS", "/NP")
    if ($ExcludeFiles.Count -gt 0) { $robocopyArgs += "/XF"; $robocopyArgs += $ExcludeFiles }
    if ($ExcludeDirs.Count -gt 0) { $robocopyArgs += "/XD"; $robocopyArgs += $ExcludeDirs }

    robocopy @robocopyArgs | Out-Null
    # Robocopy's exit codes 0-7 are all "success" (8+ means a real failure).
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed copying $Source -> $Destination (exit code $LASTEXITCODE)"
    }
}

function Wait-ForHealthy {
    param(
        [Parameter(Mandatory)] [string]$Url,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                Write-Host "$Url is healthy."
                return
            }
        } catch {
            # Not up yet — keep polling until the deadline.
        }
        Start-Sleep -Seconds 2
    }

    throw "$Url did not become healthy within $TimeoutSeconds seconds. Deploy failed — check the application event log / Serilog files before retrying."
}
