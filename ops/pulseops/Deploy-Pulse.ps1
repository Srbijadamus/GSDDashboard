# Deploy-Pulse.ps1
#
# Self-contained Pulse (GSDDashboard) deploy with automatic rollback.
# Flow (one script, no interactive steps):
#   read ACTUAL live DLL hash -> snapshot C:\ProgramData\PulseRollback\<ts>
#   -> sentinel -> stop the exact port-5000 owner -> copy (appsettings*.json NEVER
#   touched) -> remove sentinel -> Start-ScheduledTask GSDDashboard-Backend
#   -> wait /health 200 (<= 90 s).
# On failure the SAME run restores its own snapshot, restarts the task, waits 200
# and exits non-zero. On success it verifies exactly one GSDDashboard.API.exe and
# prints the running DLL hash.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File C:\PulseOps\Deploy-Pulse.ps1 `
#       -Source "C:\GSDDashboard_secure\Backend\bin\Release\net8.0"
#
# -Source points at the build output folder that contains GSDDashboard.API.dll.
# Static assets are taken from <Source>\wwwroot, or from a sibling Backend\wwwroot
# when -Source is the bin\Release\net8.0 folder.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Source
)

$ErrorActionPreference = 'Stop'

# Stop-Process on the SYSTEM-launched backend and Start/Stop-ScheduledTask both
# require an elevated session; fail early rather than half-way through the copy.
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Deploy-Pulse.ps1 must run in an elevated (Administrator) session.'
}

# ---- fixed paths (match documentation/LIVE_DEPLOY_RUNBOOK.md) ----
$live           = 'C:\GSDDashboard\Backend'
$release        = Join-Path $live 'bin\Release\net8.0'
$releaseExe     = Join-Path $release 'GSDDashboard.API.exe'
$releaseDll     = Join-Path $release 'GSDDashboard.API.dll'
$liveWwwroot    = Join-Path $live 'wwwroot'
$releaseWwwroot = Join-Path $release 'wwwroot'
$sentinel       = 'C:\HealthCheck\DEPLOY_IN_PROGRESS'
$rollbackRoot   = 'C:\ProgramData\PulseRollback'
$taskName       = 'GSDDashboard-Backend'
$port           = 5000
$healthUrl      = "http://localhost:$port/health"
$healthTimeout  = 90   # seconds

function Write-Step([string]$m) {
    Write-Host ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m) -ForegroundColor Cyan
}

function Get-Sha256([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

function Get-PortOwnerPids([int]$p) {
    @(Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
}

function Test-Health {
    try {
        $r = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5
        return ($r.StatusCode -eq 200)
    } catch { return $false }
}

function Wait-Health([int]$timeoutSec) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Health) { return $true }
        Start-Sleep -Seconds 3
    }
    return (Test-Health)
}

# ---- resolve source (build output + static assets) ----
$srcBin = $Source
if (-not (Test-Path -LiteralPath (Join-Path $srcBin 'GSDDashboard.API.dll'))) {
    throw "Build output not found in -Source '$Source' (expected GSDDashboard.API.dll)."
}
$srcWwwroot = Join-Path $srcBin 'wwwroot'
if (-not (Test-Path -LiteralPath $srcWwwroot)) {
    $sibling = Join-Path (Split-Path -Parent $srcBin) 'wwwroot'
    if (Test-Path -LiteralPath $sibling) { $srcWwwroot = $sibling }
}

# ---- preflight ----
if (Test-Path -LiteralPath $sentinel) { throw 'Another deployment is in progress (sentinel present).' }
if (-not (Test-Path -LiteralPath $releaseDll)) { throw "Live release not found: $releaseDll" }

# ---- read ACTUAL live hash ----
$liveHash = Get-Sha256 $releaseDll
$newHash  = Get-Sha256 (Join-Path $srcBin 'GSDDashboard.API.dll')
Write-Step "Live DLL hash: $liveHash"
Write-Step "New  DLL hash: $newHash"

# ---- snapshot ----
$stamp    = Get-Date -Format 'yyyyMMdd-HHmmss'
$snapshot = Join-Path $rollbackRoot $stamp
if (Test-Path -LiteralPath $snapshot) { throw "Snapshot already exists: $snapshot" }
New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
Copy-Item -LiteralPath $release -Destination (Join-Path $snapshot 'release') -Recurse -Force
if (Test-Path -LiteralPath $liveWwwroot) {
    Copy-Item -LiteralPath $liveWwwroot -Destination (Join-Path $snapshot 'wwwroot') -Recurse -Force
}
@{ stamp = $stamp; liveHashBefore = $liveHash; newHash = $newHash; source = $Source;
   createdUtc = (Get-Date).ToUniversalTime().ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $snapshot 'manifest.json') -Encoding UTF8
Write-Step "Snapshot: $snapshot"

# ---- sentinel -> stop exact PID -> copy -> remove sentinel ----
New-Item -ItemType File -Path $sentinel -Force | Out-Null
try {
    $owners = Get-PortOwnerPids $port
    foreach ($id in $owners) {
        $p = Get-CimInstance Win32_Process -Filter "ProcessId=$id" -ErrorAction SilentlyContinue
        if ($p.ExecutablePath -ne $releaseExe) {
            throw "Port $port owned by unexpected process (PID $id, '$($p.ExecutablePath)'); aborting."
        }
        Write-Step "Stopping exact port-$port owner PID $id"
        Stop-Process -Id $id -Force
    }
    Start-Sleep -Seconds 2

    # Copy build output; appsettings*.json are never replaced.
    foreach ($item in Get-ChildItem -LiteralPath $srcBin) {
        if ($item.Name -like 'appsettings*.json') { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $release -Recurse -Force
    }
    # Copy static assets to both wwwroot locations (as the promotion pipeline does).
    foreach ($dest in @($liveWwwroot, $releaseWwwroot)) {
        if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
        Copy-Item -LiteralPath $srcWwwroot -Destination $dest -Recurse -Force
    }
    # Verify index.html in BOTH static roots references the SAME bundle after the copy.
    function Get-IndexBundle([string]$p) {
        if (-not (Test-Path -LiteralPath $p)) { return $null }
        $m = [regex]::Match((Get-Content -LiteralPath $p -Raw), 'index-([A-Za-z0-9_-]+)\.js')
        if ($m.Success) { return $m.Groups[1].Value }
        return $null
    }
    $bundleLive = Get-IndexBundle (Join-Path $liveWwwroot 'index.html')
    $bundleRel  = Get-IndexBundle (Join-Path $releaseWwwroot 'index.html')
    if (-not $bundleLive -or -not $bundleRel -or $bundleLive -ne $bundleRel) {
        Write-Warning "index.html mismatch after copy (wwwroot=$bundleLive, bin\Release=$bundleRel); rolling back"
        foreach ($dest in @($liveWwwroot, $releaseWwwroot)) {
            if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
            if (Test-Path -LiteralPath (Join-Path $snapshot 'wwwroot')) {
                Copy-Item -LiteralPath (Join-Path $snapshot 'wwwroot') -Destination $dest -Recurse -Force
            }
        }
        Copy-Item -Path (Join-Path $snapshot 'release\*') -Destination $release -Recurse -Force
        throw "index.html mismatch after copy; snapshot restored from $snapshot"
    }
    Write-Step "index.html OK: both roots reference index-$bundleLive.js"
    Write-Step "Copied build -> $release (appsettings untouched)"
}
finally {
    Remove-Item -LiteralPath $sentinel -Force -ErrorAction SilentlyContinue
}

# ---- start task, wait health ----
Start-ScheduledTask -TaskName $taskName
$healthy = Wait-Health $healthTimeout

if ($healthy) {
    $procs = @(Get-CimInstance Win32_Process -Filter "Name='GSDDashboard.API.exe'")
    if ($procs.Count -ne 1) {
        Write-Warning ("Expected exactly one GSDDashboard.API.exe, found {0}." -f $procs.Count)
        exit 2
    }
    $finalHash = Get-Sha256 $releaseDll
    Write-Step "Deploy OK. Running DLL hash: $finalHash"
    Write-Host "DEPLOY SUCCESS snapshot=$snapshot hash=$finalHash"
    exit 0
}

# ---- automatic rollback from the SAME run ----
Write-Warning "Health check failed after ${healthTimeout}s; rolling back from $snapshot"
New-Item -ItemType File -Path $sentinel -Force | Out-Null
try {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    foreach ($id in (Get-PortOwnerPids $port)) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2

    Copy-Item -Path (Join-Path $snapshot 'release\*') -Destination $release -Recurse -Force
    foreach ($dest in @($liveWwwroot, $releaseWwwroot)) {
        if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
        if (Test-Path -LiteralPath (Join-Path $snapshot 'wwwroot')) {
            Copy-Item -LiteralPath (Join-Path $snapshot 'wwwroot') -Destination $dest -Recurse -Force
        }
    }
    Write-Step "Snapshot restored from $snapshot"
}
finally {
    Remove-Item -LiteralPath $sentinel -Force -ErrorAction SilentlyContinue
}

# ---- restart task, wait health ----
Start-ScheduledTask -TaskName $taskName
$rollbackHealthy = Wait-Health $healthTimeout
$rollbackHash    = Get-Sha256 $releaseDll
Write-Host ("ROLLBACK {0}; restored hash={1} (liveHashBefore={2})" -f `
    ($(if ($rollbackHealthy) { 'OK' } else { 'FAILED' })), $rollbackHash, $liveHash)
if (-not $rollbackHealthy) { exit 3 }
exit 1
