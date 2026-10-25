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
            # File locks release asynchronously -- give IIS a moment.
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

    $binding = if ($HostHeader) { "*:$($Port):$HostHeader" } else { "*:$($Port):" }

    if (-not (Test-Path "IIS:\Sites\$Name")) {
        Write-Host "Creating site '$Name' on port $Port..."
        New-Website -Name $Name -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName `
            -Port $Port -HostHeader $HostHeader -Force | Out-Null
    } else {
        Set-ItemProperty "IIS:\Sites\$Name" -Name applicationPool -Value $AppPoolName
        Set-ItemProperty "IIS:\Sites\$Name" -Name physicalPath -Value $PhysicalPath
        # BUG (found live on 116.58.33.11): this used to stop here, never touching the
        # port binding on redeploys of an already-existing site. Changing $Port (e.g. the
        # 5000 -> 5002 move after discovering 5000 was silently blocked externally) then
        # silently did nothing -- the site stayed bound to the old port while the health
        # check polled the new one, timing out with no indication why. Always reconcile
        # the binding too, not just on first creation.
        $currentBindings = Get-ItemProperty "IIS:\Sites\$Name" -Name bindings
        $alreadyBound = $currentBindings.Value | Where-Object { $_.bindingInformation -eq $binding }
        if (-not $alreadyBound) {
            Write-Host "Updating site '$Name' binding to port $Port..."
            Clear-ItemProperty "IIS:\Sites\$Name" -Name bindings
            New-ItemProperty "IIS:\Sites\$Name" -Name bindings -Value @{protocol = "http"; bindingInformation = $binding } | Out-Null
        }
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
    # Robocopy's exit codes 0-7 are all "success" (8+ means a real failure). But a
    # successful non-zero code (e.g. 1 = files copied) otherwise survives as the last
    # $LASTEXITCODE set anywhere in the script -- and since nothing else resets it,
    # powershell.exe's own process exit code mirrors it when the script ends, which
    # GitHub Actions reads as step failure even though the deploy fully succeeded
    # (discovered deploying StockOrder.Api to 116.58.33.11: "deployed and healthy"
    # printed, then "Process completed with exit code 1" anyway).
    #
    # IMPORTANT: plain `$LASTEXITCODE = 0` here only creates a function-local shadow
    # variable (PowerShell's default assignment scoping) -- it does NOT clear the real
    # automatic variable the engine set when robocopy ran, which lives at global scope.
    # That shadow disappears when this function returns, leaving the real one untouched,
    # so the bug survives even with this reset in place (found the hard way: this exact
    # fix shipped once already and the failure was identical on the next deploy). Must
    # use $global:LASTEXITCODE explicitly.
    if ($LASTEXITCODE -ge 8) {
        $code = $LASTEXITCODE
        $global:LASTEXITCODE = 0
        throw "robocopy failed copying $Source -> $Destination (exit code $code)"
    }
    $global:LASTEXITCODE = 0
}

function Wait-ForHealthy {
    param(
        [Parameter(Mandatory)] [string]$Url,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastResult = "no response"
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                Write-Host "$Url is healthy."
                return
            }
            $lastResult = "HTTP $($response.StatusCode): $($response.Content)"
        } catch {
            # Not up yet -- keep polling until the deadline, but remember why, for the report.
            $lastResult = $_.Exception.Message
            if ($_.Exception.Response) {
                try {
                    $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                    $lastResult = "HTTP $([int]$_.Exception.Response.StatusCode): $($reader.ReadToEnd())"
                } catch { }
            }
        }
        Start-Sleep -Seconds 2
    }

    Write-Host "Last health check result: $lastResult"
    throw "$Url did not become healthy within $TimeoutSeconds seconds. Deploy failed -- see the diagnostics above."
}

<#
  Prints why an app didn't come up, into the deploy log itself, so nobody has to RDP in:
  the newest Serilog file's tail and recent .NET / IIS errors from the Application event
  log (a crash before Serilog starts -- bad config, missing setting -- only shows there).
#>
function Write-StartupDiagnostics {
    param(
        [Parameter(Mandatory)] [string]$PhysicalPath,
        [Parameter(Mandatory)] [datetime]$Since
    )

    Write-Host "----- Diagnostics -----"
    $logDir = Join-Path $PhysicalPath "logs"
    $newest = Get-ChildItem -Path $logDir -Filter "*.log" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newest) {
        Write-Host "Last 60 lines of $($newest.FullName):"
        Get-Content -Path $newest.FullName -Tail 60 | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host "No log files in $logDir."
    }

    Write-Host "Application event log entries since $($Since.ToString('u')):"
    try {
        $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $Since } -MaxEvents 200 -ErrorAction Stop |
            Where-Object { $_.ProviderName -match 'AspNetCore|\.NET Runtime|Application Error|IIS' } |
            Select-Object -First 8
        if (-not $events) { Write-Host "  (none)" }
        foreach ($e in $events) {
            Write-Host "  [$($e.TimeCreated.ToString('u'))] $($e.ProviderName) ($($e.LevelDisplayName))"
            ($e.Message -split "`r?`n" | Select-Object -First 25) | ForEach-Object { Write-Host "    $_" }
        }
    } catch {
        Write-Host "  (could not read the event log: $($_.Exception.Message))"
    }
    Write-Host "----- End diagnostics -----"
}
