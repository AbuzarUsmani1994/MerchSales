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
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                Write-Host "$Url is healthy."
                return
            }
        } catch {
            # Not up yet -- keep polling until the deadline.
        }
        Start-Sleep -Seconds 2
    }

    throw "$Url did not become healthy within $TimeoutSeconds seconds. Deploy failed -- check the application event log / Serilog files before retrying."
}
