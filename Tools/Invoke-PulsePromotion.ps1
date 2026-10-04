param(
    [Parameter(Mandatory)][ValidateSet('Promote', 'Rollback')][string]$Mode,
    [Parameter(Mandatory)][string]$Snapshot
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
        New-Item -ItemType File -Path (Join-Path $Snapshot 'complete') | Out-Null
        Protect-Directory (Split-Path $productionConfig)
        Protect-ConfigFile $productionConfig
        @{ Kiosk = $stage.Kiosk } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $productionConfig -Encoding UTF8
        foreach ($item in Get-ChildItem -LiteralPath (Join-Path $source 'bin\Release\net8.0')) {
            if ($item.Name -notlike 'appsettings*.json') {
                Copy-Item -LiteralPath $item.FullName -Destination $release -Recurse -Force
            }
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
