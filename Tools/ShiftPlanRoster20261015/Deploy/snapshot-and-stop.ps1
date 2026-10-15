param(
    [string]$Snapshot = "C:\ProgramData\PulseRollback\shiftplan-roster-$(Get-Date -Format 'yyyyMMdd-HHmmss')",
    [string]$Source    = 'C:\GSDDashboard_secure\Backend\bin\Release\net8.0',
    [string]$LiveRelease = 'C:\GSDDashboard\Backend\bin\Release\net8.0',
    [string]$LiveRootWwwroot = 'C:\GSDDashboard\Backend\wwwroot',
    [string]$RuntimeConfig = 'C:\ProgramData\PulseBackend\backend.json',
    [string]$Sentinel = 'C:\HealthCheck\DEPLOY_IN_PROGRESS',
    [string]$ExpectedDllSha256 = '92CB8A5F38D8ACFDD78B4927FFAAAD3979E7FBFCB941224B3847E177A2E537C4',
    [string]$ReceiptDir = 'C:\GSDDashboard_secure\.tmp-shiftplan-roster-20261015',
    [switch]$SnapshotOnly
)
# Step 1 of the manual promote in documentation/LIVE_DEPLOY_RUNBOOK.md:
#   precondition hash -> snapshot (with hashes) -> sentinel -> stop the verified port-5000 owner.
# Precondition = the DLL live before this run (deployed 2026-10-14 18:11 by the RESIGNED fix);
# asserted before anything is written, so a different live binary stops the run.
$ErrorActionPreference = 'Stop'

function Sha([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash }

$dll = Join-Path $LiveRelease 'GSDDashboard.API.dll'
$before = Sha $dll
Write-Output "live DLL before : $before"
if ($before -ne $ExpectedDllSha256) { throw "Precondition failed: live DLL is not $ExpectedDllSha256" }
Write-Output 'precondition    : OK'

# Precondition: the watchdog owns port 5000 (never stop a process by name).
try {
    $conn = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction Stop | Select-Object -First 1
    $owner = Get-CimInstance Win32_Process -Filter "ProcessId=$($conn.OwningProcess)"
    Write-Output "port 5000 owner : $($conn.OwningProcess) $($owner.Name) parent=$($owner.ParentProcessId)"
    if ($owner.ExecutablePath -ne (Join-Path $LiveRelease 'GSDDashboard.API.exe')) {
        throw "Port 5000 is not owned by the live executable: $($owner.ExecutablePath)"
    }
} catch { throw "Port-5000 precondition failed: $($_.Exception.Message)" }

if (!(Test-Path -LiteralPath $Snapshot)) { New-Item -ItemType Directory -Path $Snapshot -Force | Out-Null }
if (Test-Path -LiteralPath (Join-Path $Snapshot 'complete')) { throw "Snapshot already complete: $Snapshot" }

$captures = @(
    @{ Name = 'release';          Path = $LiveRelease },
    @{ Name = 'release-wwwroot';  Path = (Join-Path $LiveRelease 'wwwroot') },
    @{ Name = 'wwwroot';          Path = $LiveRootWwwroot }
)
foreach ($c in $captures) {
    if (!(Test-Path -LiteralPath $c.Path)) { throw "Missing live path: $($c.Path)" }
    $dest = Join-Path $Snapshot $c.Name
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item -Path (Join-Path $c.Path '*') -Destination $dest -Recurse -Force
}
Copy-Item -LiteralPath $RuntimeConfig -Destination (Join-Path $Snapshot 'backend.json') -Force

# Hash manifest over every captured file + the runtime config.
$manifest = New-Object System.Collections.ArrayList
foreach ($c in $captures) {
    $dest = Join-Path $Snapshot $c.Name
    foreach ($f in (Get-ChildItem -LiteralPath $dest -Recurse -File)) {
        [void]$manifest.Add([pscustomobject]@{
            Path = $f.FullName.Substring($Snapshot.Length + 1)
            Hash = Sha $f.FullName
        })
    }
}
[void]$manifest.Add([pscustomobject]@{
    Path = 'backend.json'
    Hash = Sha (Join-Path $Snapshot 'backend.json')
})
$sorted = @($manifest | Sort-Object Path)
$sorted | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Snapshot 'hash-manifest.json') -Encoding UTF8

# Verify every copied file against the live original.
$bad = @()
foreach ($m in $sorted) {
    if ($m.Path -eq 'backend.json') { continue }
    $liveFile = switch -Regex ($m.Path) {
        '^release-wwwroot\\' { Join-Path (Join-Path $LiveRelease 'wwwroot') $m.Path.Substring('release-wwwroot\'.Length); break }
        '^wwwroot\\'         { Join-Path $LiveRootWwwroot $m.Path.Substring('wwwroot\'.Length); break }
        '^release\\'         { Join-Path $LiveRelease $m.Path.Substring('release\'.Length); break }
        default              { $null }
    }
    if ($liveFile -and (Sha $liveFile) -ne $m.Hash) { $bad += $m.Path }
}
if ($bad.Count) { throw "Snapshot verification failed for: $($bad -join ', ')" }
"snapshot        : $Snapshot"
"files hashed    : $($sorted.Count)"
"verified        : all copied files match the live originals"

if (!(Test-Path -LiteralPath $ReceiptDir)) { New-Item -ItemType Directory -Path $ReceiptDir -Force | Out-Null }
$Snapshot | Set-Content -LiteralPath (Join-Path $ReceiptDir 'snapshot-path.txt') -Encoding UTF8

if ($SnapshotOnly) { 'SNAPSHOT ONLY - live process untouched'; return }

# ── Sentinel first, then stop the verified port-5000 owner ───────────────────
New-Item -ItemType File -Path $Sentinel -Force | Out-Null
Write-Output "sentinel        : created $Sentinel"

$pid5000 = $conn.OwningProcess
Stop-Process -Id $pid5000 -Force
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 500
    if (!(Get-Process -Id $pid5000 -ErrorAction SilentlyContinue)) { break }
}
if (Get-Process -Id $pid5000 -ErrorAction SilentlyContinue) { throw 'Live process did not stop.' }
Write-Output 'live process    : stopped'
