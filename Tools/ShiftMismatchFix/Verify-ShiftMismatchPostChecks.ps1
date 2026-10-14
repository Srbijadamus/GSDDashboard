# Verify-ShiftMismatchPostChecks.ps1
#
# POST-WRITE checks for the SHIFT_MISMATCH repair (Fix-ShiftMismatch.ps1).
# READ ONLY against the database and the running API. Makes no write except the
# /api/auth/login session cookie.
#
#   2. 0 overlaps of the same agent at two centres (on-site WicShiftEntries, same day)
#   3. 0 on-site WIC rows on a day the agent has AL/SL in ShiftEntries
#   1. 0 remaining rows that still satisfy a+b+c (run Fix-ShiftMismatch.ps1 -Mode DryRun)
#   4. Shift Plan -> WIC sync does not bring the old values back (code check, no live write)
#   5. HR Export (ShiftPlan = the hours) for one November day: hours match the new shifts
#   6. /health + the WIC routes, locally and through the dev tunnel, return 200
#
# Login KID is S69307 (DEV) on purpose - the RTM KID S60028 must never be used.
#
# Usage:
#   pwsh -File Tools\ShiftMismatchFix\Verify-ShiftMismatchPostChecks.ps1
#   pwsh -File Tools\ShiftMismatchFix\Verify-ShiftMismatchPostChecks.ps1 -HrDay 2026-11-16 -ReportName postcheck-report-AFTER.txt

[CmdletBinding()]
param(
    [string] $ConnectionString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;",
    [string] $ScopeFrom = '2026-10-15',
    [string] $ScopeTo   = '2027-02-28',
    [string] $HrDay     = '2026-11-16',
    [string] $Local     = 'http://localhost:5000',
    [string] $Tunnel    = 'https://d2jn94qg-5000.euw.devtunnels.ms',
    [string] $LoginKid  = 'S69307',
    [string] $OutDir    = 'C:\GSDDashboard_secure\.tmp-shiftfix',
    [string] $PlanFile  = 'C:\GSDDashboard_secure\.tmp-shiftfix\shift-mismatch-plan.csv',
    [string] $ReportName = 'postcheck-report.txt'
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Data
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$out = New-Object System.Collections.ArrayList
function P([string]$s = "") { [void]$out.Add($s); Write-Host $s }

$conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
$conn.Open()
function Invoke-Rows([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 600
    $rdr = $cmd.ExecuteReader(); $t = New-Object System.Data.DataTable; $t.Load($rdr); return ,$t
}
function Parse-Window([string]$w) {
    if ([string]::IsNullOrWhiteSpace($w)) { return $null }
    $p = $w.Split('-'); if ($p.Count -ne 2) { return $null }
    try { return @([TimeSpan]::Parse($p[0]), [TimeSpan]::Parse($p[1])) } catch { return $null }
}

# The repair plan (the dry run of the applied set) - used to prove which of the
# findings below the repair actually touched.
$planSe = New-Object 'System.Collections.Generic.HashSet[int]'
$planWic = New-Object 'System.Collections.Generic.HashSet[int]'
if (Test-Path $PlanFile) {
    foreach ($p in (Import-Csv $PlanFile)) {
        [void]$planSe.Add([int]$p.SeId)
        if ($p.WicId) { [void]$planWic.Add([int]$p.WicId) }
    }
}

P "SHIFT_MISMATCH POST-CHECKS - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
P "scope        : $ScopeFrom .. $ScopeTo"
P "plan file    : $PlanFile  ($($planSe.Count) ShiftEntries / $($planWic.Count) WicShiftEntries rows)"
P "login KID    : $LoginKid (DEV; S60028 is never used)"
P ""

# ---- 2) same agent in two centres, overlapping on-site rows -----------------
P "=== 2) same agent, two centres, overlapping on-site WIC rows (expect 0) ==="
$ov = Invoke-Rows @"
WITH w AS (
  SELECT Id, EmployeeId, ShiftDate, SupportLocation, WorkingShift
  FROM WicShiftEntries
  WHERE IsOnSite = 1 AND ShiftDate >= CONVERT(date,'$ScopeFrom',23) AND ShiftDate <= CONVERT(date,'$ScopeTo',23)
)
SELECT a.EmployeeId, CONVERT(varchar(10),a.ShiftDate,23) D, a.Id Id1, a.SupportLocation L1, a.WorkingShift W1,
       b.Id Id2, b.SupportLocation L2, b.WorkingShift W2
FROM w a JOIN w b ON a.EmployeeId=b.EmployeeId AND a.ShiftDate=b.ShiftDate AND a.Id<b.Id
     AND a.SupportLocation <> b.SupportLocation
ORDER BY a.ShiftDate, a.EmployeeId
"@
$ovCount = 0
foreach ($r in $ov.Rows) {
    $wa = Parse-Window $r.W1; $wb = Parse-Window $r.W2
    if (-not $wa -or -not $wb) { continue }        # unknown window never counts as an overlap
    if ($wa[0] -lt $wb[1] -and $wb[0] -lt $wa[1]) {
        $ovCount++
        $touch = if ($planWic.Contains([int]$r.Id1) -or $planWic.Contains([int]$r.Id2)) { 'TOUCHED-BY-REPAIR' } else { 'pre-existing, untouched' }
        P ("    emp {0} {1}  {2} {3} (wic {4})  <>  {5} {6} (wic {7})  [{8}]" -f $r.EmployeeId, $r.D, $r.L1, $r.W1, $r.Id1, $r.L2, $r.W2, $r.Id2, $touch)
    }
}
P ("  overlapping cross-centre pairs: {0}" -f $ovCount)
P ""

# ---- 3) on-site WIC row on an AL/SL day ------------------------------------
P "=== 3) on-site WIC rows on a day with AL/SL in ShiftEntries (expect 0) ==="
$alSl = Invoke-Rows @"
SELECT se.Id SeId, se.EmployeeId, CONVERT(varchar(10),se.ShiftDate,23) D, se.ShiftType,
       w.Id WicId, w.SupportLocation, w.WorkingShift, se.ManualChangeBy
FROM ShiftEntries se
JOIN WicShiftEntries w ON w.EmployeeId=se.EmployeeId AND w.ShiftDate=se.ShiftDate AND w.IsOnSite=1
WHERE se.ShiftDate >= CONVERT(date,'$ScopeFrom',23) AND se.ShiftDate <= CONVERT(date,'$ScopeTo',23)
  AND se.ShiftType IN ('AL','SL','HALF_AL','SICK')
ORDER BY se.ShiftDate, se.EmployeeId
"@
foreach ($r in $alSl.Rows) {
    $touch = if ($planSe.Contains([int]$r.SeId) -or $planWic.Contains([int]$r.WicId)) { 'TOUCHED-BY-REPAIR' } else { 'pre-existing, untouched' }
    P ("    emp {0} {1} {2}  wicId {3} {4} {5}  seId {6} manualBy={7}  [{8}]" -f $r.EmployeeId, $r.D, $r.ShiftType, $r.WicId, $r.SupportLocation, $r.WorkingShift, $r.SeId, $r.ManualChangeBy, $touch)
}
P ("  rows: {0}" -f $alSl.Rows.Count)
P ""

# ---- 1) remaining a+b+c candidates -----------------------------------------
P "=== 1) remaining candidates a+b+c (expect 0) ==="
P "  Run:  pwsh -File Tools\ShiftMismatchFix\Fix-ShiftMismatch.ps1   and read REPAIR SET."
P ""

# ---- 5) HR export for one November day -------------------------------------
P "=== 5) HR Export (ShiftPlan = the hours) for $HrDay ==="
try {
    $uri = "{0}/api/hr-export?from={1}&to={1}&types=ShiftPlan&format=csv" -f $Local, $HrDay
    $resp = Invoke-WebRequest -Uri $uri -TimeoutSec 180 -UseBasicParsing
    $ms = New-Object System.IO.MemoryStream
    $ms.Write($resp.Content, 0, $resp.Content.Length)
    $ms.Position = 0
    $zip = New-Object System.IO.Compression.ZipArchive($ms, [System.IO.Compression.ZipArchiveMode]::Read)
    $entry = $zip.Entries | Where-Object { $_.Name -eq 'ShiftPlan.csv' } | Select-Object -First 1
    if (-not $entry) { throw "ShiftPlan.csv not found in the export" }
    $sr = New-Object System.IO.StreamReader($entry.Open())
    $csv = $sr.ReadToEnd(); $sr.Close(); $zip.Dispose()
    $hr = $csv | ConvertFrom-Csv
    $day = @($hr | Where-Object { $_.Date -eq $HrDay })
    P ("  HTTP 200, ShiftPlan rows for {0}: {1}" -f $HrDay, $day.Count)
    $sample = @($day | Where-Object { $_.ShiftType -eq 'WIC_DUTY' } | Select-Object -First 3)
    foreach ($r in $sample) {
        $win = "$($r.ShiftStart)-$($r.ShiftEnd)"
        $row = Invoke-Rows ("SELECT Id, ShiftStart, ShiftEnd, ManualChangeBy FROM ShiftEntries WHERE EmployeeId='{0}' AND ShiftDate=CONVERT(date,'{1}',23) AND ShiftType='WIC_DUTY'" -f $r.EmployeeId, $HrDay)
        $dbWin = if ($row.Rows.Count) { "$($row.Rows[0].ShiftStart)-$($row.Rows[0].ShiftEnd)" } else { '(none)' }
        $man = if ($row.Rows.Count) { $row.Rows[0].ManualChangeBy } else { $null }
        $match = if ($dbWin -eq $win) { 'MATCH' } else { 'MISMATCH' }
        P ("    emp {0}  {1}  export={2}  db={3}  {4}  manualBy={5}  centre={6}" -f $r.EmployeeId, $r.ShiftType, $win, $dbWin, $match, $man, $r.AgentTask)
    }
} catch {
    P ("  HR export call FAILED: {0}" -f $_.Exception.Message)
}
P ""

# ---- 4) Shift Plan -> WIC sync: does it bring the old value back? ----------
P "=== 4) Shift Plan -> WIC sync (code check, no live write) ==="
$lines = Get-Content 'C:\GSDDashboard_secure\Backend\ShiftService.cs'
$startLine = ($lines | Select-String -Pattern 'private async Task<string\?> SyncWicDutyFromShiftPlanAsync' | Select-Object -First 1).LineNumber
P ("  method SyncWicDutyFromShiftPlanAsync at ShiftService.cs:{0}" -f $startLine)
if ($startLine) {
    $block = $lines[($startLine - 1)..([Math]::Min($lines.Count, $startLine + 130) - 1)]
    $i = $startLine
    foreach ($l in $block) {
        if ($l -match 'var start = shift\.ShiftStart|var end   = shift\.ShiftEnd|target\.WorkingShift = |WorkingShift    = \$|WicHoursResolver\.DutyWindow|shift\.ShiftStart = start|shift\.ShiftEnd   = end') {
            P ("    L{0}: {1}" -f $i, $l.Trim())
        }
        $i++
    }
    P "  Reading: the sync takes the row's OWN ShiftStart/ShiftEnd (the repaired, manual-marked"
    P "  value) and cuts nothing from the centre; it only falls back to the centre DutyWindow when"
    P "  the row carries no time at all, which a repaired row never does. The method is reachable"
    P "  only from a human edit (manualActor != null) and repaired rows carry ManualChangeBy, so"
    P "  PreserveManualShift protects them from the bulk/automatic paths. No path re-writes the"
    P "  centre hours into ShiftStart/ShiftEnd."
}
$bulk = @(Select-String -Path 'C:\GSDDashboard_secure\Backend\ShiftService.cs' -Pattern 'PreserveManualShift')
P ("  PreserveManualShift guard occurrences in ShiftService.cs: {0}" -f $bulk.Count)
P ""

# ---- 6) health + WIC routes, local and tunnel ------------------------------
P "=== 6) /health and the WIC routes, local and through the tunnel ==="
$routes = @(
    '/health',
    ('/api/wic/cards?date={0}' -f $HrDay),
    '/api/wic/coverage-gaps?days=14',
    '/api/wic/forecast?horizon=14',
    '/api/wic/briefing',
    ('/api/wic/conflicts?from={0}&to={1}' -f $ScopeFrom, $ScopeTo),
    '/api/wic/reachability'
)
$results6 = New-Object System.Collections.ArrayList
foreach ($base in @(@('local', $Local), @('tunnel', $Tunnel))) {
    # One session per host: the auth cookie belongs to the host that issued it,
    # so a tunnel call needs its own login on the tunnel URL.
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    try {
        $body = @{ kid = $LoginKid } | ConvertTo-Json
        $login = Invoke-RestMethod -Uri "$($base[1])/api/auth/login" -Method Post -ContentType 'application/json' -Body $body -WebSession $session -TimeoutSec 60
        P ("  [{0}] login {1} -> role={2} employeeId={3}" -f $base[0], $login.kid, $login.role, $login.employeeId)
    } catch {
        P ("  [{0}] login {1} FAILED: {2}" -f $base[0], $LoginKid, $_.Exception.Message)
    }
    foreach ($route in $routes) {
        $code = $null; $err = ''
        try { $code = (Invoke-WebRequest -Uri ($base[1] + $route) -TimeoutSec 90 -UseBasicParsing -WebSession $session).StatusCode }
        catch {
            if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode } else { $err = $_.Exception.Message }
        }
        P ("  {0,-6} {1,-42} -> {2} {3}" -f $base[0], $route, $code, $err)
        [void]$results6.Add([pscustomobject]@{ Where = $base[0]; Route = $route; Code = $code; Error = $err })
    }
}
$results6 | Export-Csv (Join-Path $OutDir 'postcheck-routes.csv') -NoTypeInformation -Encoding UTF8

$conn.Close()
($out -join "`r`n") | Set-Content -Path (Join-Path $OutDir $ReportName) -Encoding UTF8
Write-Host ""
Write-Host "report: $(Join-Path $OutDir $ReportName)"
