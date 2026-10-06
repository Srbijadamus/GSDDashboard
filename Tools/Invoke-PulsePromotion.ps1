param(
    [Parameter(Mandatory)][ValidateSet('Promote', 'Rollback')][string]$Mode,
    [Parameter(Mandatory)][string]$Snapshot,
    [switch]$MaintenanceWithoutWatchdog
)
$ErrorActionPreference = 'Stop'
if ($Mode -eq 'Promote') {
    & (Join-Path $PSScriptRoot 'Assert-PulsePreservation.ps1') -RequireBuild
}
if ($Snapshot -notmatch '^C:\\ProgramData\\PulseRollback\\[0-9]{8}-[0-9]{6}$') {
    throw 'Snapshot must be a timestamp directory under C:\ProgramData\PulseRollback.'
}
$live = 'C:\GSDDashboard\Backend'
$release = Join-Path $live 'bin\Release\net8.0'
$source = 'C:\GSDDashboard_secure\Backend'
$productionConfig = 'C:\ProgramData\PulseBackend\backend.json'
$stageConfig = 'C:\ProgramData\PulseStage\backend.json'
$sentinel = 'C:\HealthCheck\DEPLOY_IN_PROGRESS'
$importFiles = @('import_shifts.py', 'import_shifts_xl.py', 'shift_import_guard.py')
if ($MaintenanceWithoutWatchdog) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Maintenance promotion requires an enabled Administrators token.'
    }
    $task = Get-ScheduledTask -TaskName GSDDashboard-Backend
    $watchdogs = @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
        Where-Object { $_.CommandLine -like '*watchdog_gsd_backend.ps1*' })
    if ($task.Settings.Enabled -or $watchdogs.Count) {
        throw 'Maintenance promotion requires the production watchdog disabled and stopped.'
    }
}
if (Test-Path -LiteralPath $sentinel) { throw 'Another deployment is in progress.' }
if ($Mode -eq 'Promote' -and (Test-Path -LiteralPath $Snapshot)) { throw 'Snapshot already exists.' }
if ($Mode -eq 'Rollback' -and !(Test-Path -LiteralPath (Join-Path $Snapshot 'complete'))) {
    throw 'Complete rollback snapshot missing.'
}
foreach ($path in @($live, $release, $source, $Snapshot, (Split-Path $Snapshot), (Split-Path $productionConfig))) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Reparse-point deployment path rejected.'
    }
}
if ($Mode -eq 'Promote') {
    if (!(Test-Path -LiteralPath (Join-Path $source 'bin\Release\net8.0\GSDDashboard.API.dll'))) { throw 'Build first.' }
    $stage = Get-Content -LiteralPath $stageConfig -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($stage.Kiosk.ServiceToken) -or $stage.Kiosk.BaseUrl -ne 'http://localhost:8000') {
        throw 'Service credential/configuration missing.'
    }
}
function Protect-Directory([string]$Path) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
            (New-Object System.Security.Principal.SecurityIdentifier($sid)),
            'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}
function Protect-ConfigFile([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { New-Item -ItemType File -Path $Path | Out-Null }
    $acl = New-Object System.Security.AccessControl.FileSecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
            (New-Object System.Security.Principal.SecurityIdentifier($sid)), 'FullControl', 'Allow')))
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}
if ($Mode -eq 'Promote') {
    $owners = @(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    if (!$MaintenanceWithoutWatchdog -and $owners.Count -ne 1) { throw 'Exactly one running production Pulse process required for watchdog-context preflight.' }
    foreach ($owner in $owners) {
        $running = Get-CimInstance Win32_Process -Filter "ProcessId=$owner"
        if ($running.ExecutablePath -ne (Join-Path $release 'GSDDashboard.API.exe')) { throw 'Unexpected production executable.' }
        $watchdogProcessId = $running.ParentProcessId
    }
    $preflightRoot = 'C:\ProgramData\PulsePreflight'
    if ((Test-Path -LiteralPath $preflightRoot) -and
        ((Get-Item -LiteralPath $preflightRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Reparse-point preflight root rejected.'
    }
    Protect-Directory $preflightRoot
    $preflightDirectory = Join-Path $preflightRoot (Split-Path $Snapshot -Leaf)
    if (Test-Path -LiteralPath $preflightDirectory) { throw 'Preflight directory already exists; never reuse receipts.' }
    Protect-Directory $preflightDirectory
    $preflightConfig = Join-Path $preflightDirectory 'backend.json'
    Protect-ConfigFile $preflightConfig
    @{ Kiosk = $stage.Kiosk } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $preflightConfig -Encoding UTF8
    # Duplicate the running watchdog token, not the maintenance operator token.
    if ($MaintenanceWithoutWatchdog) {
        $previousConfig = $env:PULSE_RUNTIME_CONFIG
        Push-Location $live
        try {
            $env:PULSE_RUNTIME_CONFIG = $preflightConfig
            & (Join-Path $source 'bin\Release\net8.0\GSDDashboard.API.exe') --Pulse:ConfigPreflight=true
            if ($LASTEXITCODE) { throw 'Administrator maintenance configuration preflight failed.' }
        } finally {
            $env:PULSE_RUNTIME_CONFIG = $previousConfig
            Pop-Location
        }
    } else {
        & (Join-Path $PSScriptRoot 'Invoke-PulseWatchdogContext.ps1') `
            -WatchdogProcessId $watchdogProcessId `
            -Executable (Join-Path $source 'bin\Release\net8.0\GSDDashboard.API.exe') `
            -WorkingDirectory $live -RuntimeConfig $preflightConfig `
            -Receipt (Join-Path $preflightDirectory 'watchdog-before-maintenance.json')
    }
}
New-Item -ItemType File -Path $sentinel | Out-Null
try {
    $listeners = @(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    foreach ($id in $listeners) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$id"
        if ($process.ExecutablePath -ne (Join-Path $release 'GSDDashboard.API.exe')) {
            throw 'Port 5000 is not owned by the expected Pulse executable; stop.'
        }
    }
    foreach ($id in $listeners) { Stop-Process -Id $id -Force }
    Start-Sleep -Seconds 2
    if ($Mode -eq 'Promote') {
        Protect-Directory $Snapshot
        Copy-Item -LiteralPath $release -Destination (Join-Path $Snapshot 'release') -Recurse
        Copy-Item -LiteralPath (Join-Path $live 'wwwroot') -Destination (Join-Path $Snapshot 'wwwroot') -Recurse
        if (Test-Path -LiteralPath $productionConfig) {
            Copy-Item -LiteralPath $productionConfig -Destination (Join-Path $Snapshot 'backend.json')
        }
        $importSnapshot = Join-Path $Snapshot 'imports'
        Protect-Directory $importSnapshot
        $importInventory = foreach ($name in $importFiles) {
            $path = Join-Path (Split-Path $live) $name
            $exists = Test-Path -LiteralPath $path
            if ($exists) { Copy-Item -LiteralPath $path -Destination (Join-Path $importSnapshot $name) }
            [pscustomobject]@{ Name = $name; Existed = $exists }
        }
        $importInventory | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $importSnapshot 'inventory.json') -Encoding UTF8
        New-Item -ItemType File -Path (Join-Path $Snapshot 'complete') | Out-Null
        Protect-Directory (Split-Path $productionConfig)
        Protect-ConfigFile $productionConfig
        @{ Kiosk = $stage.Kiosk } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $productionConfig -Encoding UTF8
        $previousConfig = $env:PULSE_RUNTIME_CONFIG
        Push-Location $live
        try {
            $env:PULSE_RUNTIME_CONFIG = $null
            & (Join-Path $source 'bin\Release\net8.0\GSDDashboard.API.exe') --Pulse:ConfigPreflight=true
            if ($LASTEXITCODE) { throw 'Production runtime configuration preflight failed; stop and roll back.' }
        } finally {
            $env:PULSE_RUNTIME_CONFIG = $previousConfig
            Pop-Location
        }
        if (!$MaintenanceWithoutWatchdog) {
            & (Join-Path $PSScriptRoot 'Invoke-PulseWatchdogContext.ps1') `
                -WatchdogProcessId $watchdogProcessId `
                -Executable (Join-Path $source 'bin\Release\net8.0\GSDDashboard.API.exe') `
                -WorkingDirectory $live -RuntimeConfig $productionConfig `
                -Receipt (Join-Path $Snapshot 'watchdog-production-config-preflight.json')
        }
        foreach ($item in Get-ChildItem -LiteralPath (Join-Path $source 'bin\Release\net8.0')) {
            if ($item.Name -notlike 'appsettings*.json') {
                Copy-Item -LiteralPath $item.FullName -Destination $release -Recurse -Force
            }
        }
        foreach ($name in $importFiles) {
            Copy-Item -LiteralPath (Join-Path (Split-Path $source) $name) -Destination (Join-Path (Split-Path $live) $name) -Force
        }
        $assets = Join-Path $source 'wwwroot'
    } else {
        Copy-Item -Path (Join-Path $Snapshot 'release\*') -Destination $release -Recurse -Force
        if (Test-Path -LiteralPath (Join-Path $Snapshot 'backend.json')) {
            Protect-Directory (Split-Path $productionConfig)
            Protect-ConfigFile $productionConfig
            Copy-Item -LiteralPath (Join-Path $Snapshot 'backend.json') -Destination $productionConfig -Force
        } elseif (Test-Path -LiteralPath $productionConfig) {
            Remove-Item -LiteralPath $productionConfig -Force
        }
        $inventoryPath = Join-Path $Snapshot 'imports\inventory.json'
        if (Test-Path -LiteralPath $inventoryPath) {
            foreach ($entry in (Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json)) {
                $path = Join-Path (Split-Path $live) $entry.Name
                if ($entry.Existed) {
                    Copy-Item -LiteralPath (Join-Path $Snapshot "imports\$($entry.Name)") -Destination $path -Force
                } elseif (Test-Path -LiteralPath $path) {
                    Remove-Item -LiteralPath $path -Force
                }
            }
        }
        $assets = Join-Path $Snapshot 'wwwroot'
    }
    foreach ($destination in @((Join-Path $live 'wwwroot'), (Join-Path $release 'wwwroot'))) {
        # Explicit static-content paths only; no configuration or database cleanup.
        if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
        Copy-Item -LiteralPath $assets -Destination $destination -Recurse
    }
    Write-Output "$Mode complete; snapshot: $Snapshot. Watchdog resumes after sentinel removal."
} catch {
    Write-Warning "Promotion/rollback stopped. Keep the snapshot and run Rollback if a complete snapshot exists."
    throw
} finally {
    Remove-Item -LiteralPath $sentinel -Force
}
