# Backfill-WicDutyCandidates.ps1
#
# READ-ONLY BY DEFAULT (dry run).  SELECT statements + a dry-run plan only.
# The ONLY mode that writes anything to the database is an explicit -Apply.
#
# Scope (the "task3" set):
#   ShiftEntries rows with ShiftType='WIC_DUTY' and ShiftDate >= AsOf (default:
#   server today) for which NO WicShiftEntries row exists for the same
#   EmployeeId+ShiftDate.  Each such row is then tested against five checks:
#
#   a) the agent is active and NOT absent that day (AL / SICK / public holiday)
#   b) the centre named by AgentTask is OPEN that day per WicOpeningHours
#      (exception day first, then the weekday pattern, latest EffectiveFrom <= date)
#   c) the agent holds MAIN / BACKUP / REGIONAL for that centre
#      (WicAgentAssignments, active)
#   d') the row's ShiftStart-ShiftEnd differs from this EMPLOYEE'S USUAL SHIFT
#      -> flagged SHIFT_MISMATCH.  The usual shift is the most frequent
#      ShiftStart-ShiftEnd over the employee's own manual rows (ManualChangeBy
#      set) and WORKING rows of the last 8 weeks.  An employee with fewer than
#      MinBaselineRows usable baseline observations -> NO_BASELINE.
#      A SHIFT_MISMATCH or NO_BASELINE row is REPORTED, not excluded.
#   e) the centre is already covered by another agent (>=1 on-site
#      WicShiftEntries row, active employee) and adding this agent would push
#      the centre above its minRequired -> flagged DOUBLE (possible double plan).
#
# BACKFILL SET (the dry-run plan): every row that passes a, b and c and is NOT
# DOUBLE — SHIFT_MISMATCH and NO_BASELINE rows are INCLUDED, exactly as the
# live backfill is meant to restore visibility.  For each such row the plan
# mirrors SyncWicDutyFromShiftPlanAsync: one WicShiftEntries row with
#   WorkingShift = the ShiftEntries shift CUT TO the centre's working window,
#   IsOnSite=1, Task='WIC', LocationCode/SupportLocation = the centre.
# ShiftEntries itself is never changed.
#
# Reports (per month and per check) are written in both modes.
#
# Mirrors the server rules:
#   WicHoursResolver.Resolve / DutyWindow        Backend/Services/WicHoursResolver.cs
#   PublicHolidayResolver.Applies                Backend/Services/PublicHolidayResolver.cs
#   CoverageEvaluator.EvaluateAsync              Backend/Services/CoverageEvaluator.cs
#   roster batch writer (writer #1)              Backend/RosterService.cs:763-764, 918-929, 1091-1092
#   SyncWicDutyFromShiftPlanAsync                Backend/ShiftService.cs:325+ (WicShiftEntries shape)
#
# Examples:
#   # dry run (default): checks + report + dry-run plan, no writes
#   pwsh -File Tools\Backfill-WicDutyCandidates.ps1
#
#   # explicit dry run (same as default)
#   pwsh -File Tools\Backfill-WicDutyCandidates.ps1 -WhatIf
#
#   # the only writing mode (NOT part of the read-only investigation)
#   pwsh -File Tools\Backfill-WicDutyCandidates.ps1 -Apply -ExpectedInsertCount <n>

[CmdletBinding()]
param(
    [string] $ConnectionString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;",
    [string] $AsOf             = "",      # scope: ShiftDate >= AsOf (default: server today)
    [string] $OutDir           = "C:\GSDDashboard_secure\.tmp-d94-select",
    [string] $ChecksCsv        = "",      # per-row verdicts
    [string] $ReportFile       = "",      # monthly summary + per-check failures
    [string] $DryRunFile       = "",      # the dry-run plan
    [int]    $BaselineWeeks    = 8,       # usual-shift window, in weeks, ending today
    [int]    $MinBaselineRows  = 3,       # fewer usable baseline rows -> NO_BASELINE
    [int]    $ExpectedInsertCount = 0,    # -Apply: refuse unless the plan has exactly this many rows
    [string] $InsertedIdsFile  = "",      # -Apply: new WicShiftEntries ids
    [string] $RollbackSqlFile  = "",      # -Apply: generated rollback script (never executed here)
    [switch] $WhatIf,                     # belt-and-braces dry run (default is dry run anyway)
    [switch] $Apply                       # EXPLICIT write opt-in; default is dry run
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Data
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$isDryRun = (-not $Apply) -or $WhatIf
$mode     = if ($isDryRun) { "DRY RUN (no writes)" } else { "APPLY (writes INSERTs)" }

if ([string]::IsNullOrWhiteSpace($ChecksCsv))  { $ChecksCsv  = Join-Path $OutDir "wic_backfill_checks.csv" }
if ([string]::IsNullOrWhiteSpace($ReportFile)) { $ReportFile = Join-Path $OutDir "wic_backfill_report.txt" }
if ([string]::IsNullOrWhiteSpace($DryRunFile)) { $DryRunFile = "C:\GSDDashboard_secure\Tools\Backfill-WicDutyCandidates.dryrun.txt" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# ---------------------------------------------------------------------------
# Read-only plumbing
# ---------------------------------------------------------------------------
$conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
$conn.Open()

function Invoke-Rows([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 600
    $rdr = $cmd.ExecuteReader()
    $out = New-Object System.Collections.ArrayList
    while ($rdr.Read()) {
        $h = [ordered]@{}
        for ($i = 0; $i -lt $rdr.FieldCount; $i++) {
            $v = $rdr.GetValue($i); if ($v -is [System.DBNull]) { $v = $null }
            $h[$rdr.GetName($i)] = $v
        }
        [void]$out.Add([pscustomobject]$h)
    }
    $rdr.Close()
    return ,$out
}
function Invoke-Scalar([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 600
    return $cmd.ExecuteScalar()
}

# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------
function Repair-Moji([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return $s }
    try { return [System.Text.Encoding]::UTF8.GetString([System.Text.Encoding]::GetEncoding(1252).GetBytes($s)) }
    catch { return $s }
}
function Test-BundeslandMatch([string]$a, [string]$b) {
    if ([string]::IsNullOrWhiteSpace($a) -or [string]::IsNullOrWhiteSpace($b)) { return $false }
    $A = $a.Trim(); $B = $b.Trim()
    if ($A -ieq $B) { return $true }
    $rA = Repair-Moji $A; $rB = Repair-Moji $B
    return ($rA -ieq $rB) -or ($rA -ieq $B) -or ($A -ieq $rB)
}
function Normalize-Time([string]$t) {
    if ([string]::IsNullOrWhiteSpace($t)) { return $null }
    $t = $t.Trim()
    $ts = [TimeSpan]::Zero
    if ([TimeSpan]::TryParse($t, [ref]$ts)) { return ('{0:00}:{1:00}' -f [int]$ts.TotalHours, $ts.Minutes) }
    return $null
}
function Normalize-Window([string]$s, [string]$e) {
    $ns = Normalize-Time $s; $ne = Normalize-Time $e
    if (-not $ns -or -not $ne) { return $null }
    return "$ns-$ne"
}
# The part of one "HH:mm-HH:mm" window that lies inside another (the centre's
# duty window). Null when they do not overlap — the plan then writes no row.
function Get-IntersectionWindow([string]$win, [string]$centreWin) {
    if (-not $win -or -not $centreWin) { return $null }
    $w  = $win.Split('-'); $c = $centreWin.Split('-')
    if ($w.Count -ne 2 -or $c.Count -ne 2) { return $null }
    $ws = [TimeSpan]::Parse($w[0]); $we = [TimeSpan]::Parse($w[1])
    $cs = [TimeSpan]::Parse($c[0]); $ce = [TimeSpan]::Parse($c[1])
    $s = if ($ws -gt $cs) { $ws } else { $cs }
    $e = if ($we -lt $ce) { $we } else { $ce }
    if ($e -le $s) { return $null }
    return ('{0:00}:{1:00}-{2:00}:{3:00}' -f [int]$s.TotalHours, $s.Minutes, [int]$e.TotalHours, $e.Minutes)
}
function Get-DutyWindow($h) {
    if (-not $h -or $h.IsClosed) { return $null }
    $start = if (-not [string]::IsNullOrWhiteSpace($h.OpenTime))  { $h.OpenTime }  else { $h.OpenTime2 }
    $end   = if (-not [string]::IsNullOrWhiteSpace($h.CloseTime2)) { $h.CloseTime2 } else { $h.CloseTime }
    return Normalize-Window $start $end
}

# ---------------------------------------------------------------------------
# Load reference data (SELECT only)
# ---------------------------------------------------------------------------
$serverToday = [string](Invoke-Scalar "SELECT CONVERT(varchar(10), CAST(GETDATE() AS date), 23)")
$scopeFrom   = if ([string]::IsNullOrWhiteSpace($AsOf)) { $serverToday } else { $AsOf }
$baselineFrom = [string](Invoke-Scalar ("SELECT CONVERT(varchar(10), DATEADD(day, -{0}, CAST(GETDATE() AS date)), 23)" -f ($BaselineWeeks * 7 - 1)))

$rows = Invoke-Rows @"
SELECT se.Id, se.EmployeeId, CONVERT(varchar(10), se.ShiftDate, 23) AS D,
       DATENAME(weekday, se.ShiftDate) AS Dow, se.AgentTask,
       se.ShiftStart, se.ShiftEnd, se.SourceModule, se.SourceId,
       se.AutoGenerated, se.ManualChangeBy
FROM ShiftEntries se
WHERE se.ShiftType = 'WIC_DUTY'
  AND se.ShiftDate >= CONVERT(date, '$scopeFrom', 23)
  AND NOT EXISTS (SELECT 1 FROM WicShiftEntries w
                  WHERE w.EmployeeId = se.EmployeeId AND w.ShiftDate = se.ShiftDate)
ORDER BY se.ShiftDate, se.EmployeeId, se.Id
"@

# The USUAL SHIFT baseline: an employee's own manual rows (ManualChangeBy set)
# and WORKING rows of the last BaselineWeeks weeks, both times present.
$baseRows = Invoke-Rows @"
SELECT se.Id, se.EmployeeId, CONVERT(varchar(10), se.ShiftDate, 23) AS D,
       se.ShiftType, se.ShiftStart, se.ShiftEnd, se.ManualChangeBy
FROM ShiftEntries se
WHERE se.ShiftDate >= CONVERT(date, '$baselineFrom', 23)
  AND se.ShiftDate <= CONVERT(date, '$serverToday', 23)
  AND (se.ManualChangeBy IS NOT NULL OR se.ShiftType = 'WORKING')
ORDER BY se.EmployeeId, se.ShiftDate, se.Id
"@

$locs  = Invoke-Rows "SELECT LocationCode, DisplayName, LocationCodeLegacy, IsActive, MinAgentsRequired, Bundesland, City FROM WicLocations"
$hours = Invoke-Rows "SELECT Id, LocationCode, DayOfWeek, OpenTime, CloseTime, OpenTime2, CloseTime2, IsClosed, MinRequired, CONVERT(varchar(10),ExceptionDate,23) ExDate, CONVERT(varchar(10),EffectiveFrom,23) EffFrom, CONVERT(varchar(23),ManualChangeAt,121) MAt FROM WicOpeningHours"
$emps  = Invoke-Rows "SELECT EmployeeId, FullName, IsActive, Bundesland FROM Employees"
$assign= Invoke-Rows "SELECT LocationCode, EmployeeName, AssignmentType FROM WicAgentAssignments WHERE IsActive = 1"
$wic   = Invoke-Rows "SELECT w.Id, w.EmployeeId, CONVERT(varchar(10), w.ShiftDate, 23) AS D, w.SupportLocation, w.IsOnSite, w.Task, w.WorkingShift FROM WicShiftEntries w WHERE w.ShiftDate >= CONVERT(date, '$scopeFrom', 23)"
$sick  = Invoke-Rows "SELECT EmployeeId, CONVERT(varchar(10), FirstDay, 23) F, CONVERT(varchar(10), LastDay, 23) L FROM SickLeaves"
$vac   = Invoke-Rows "SELECT EmployeeId, CONVERT(varchar(10), FirstDay, 23) F, CONVERT(varchar(10), LastDay, 23) L, ApprovedDenied FROM Vacations"
$hols  = Invoke-Rows "SELECT CONVERT(varchar(10), HolidayDate, 23) D, Name, IsNational, Bundesland FROM PublicHolidays"
$batches = Invoke-Rows "SELECT Id, EmployeeId, CONVERT(varchar(10), DateFrom, 23) DF, CONVERT(varchar(10), DateTo, 23) DT, ShiftStart, ShiftEnd, LocationCode, CONVERT(varchar(19), CreatedAt, 120) CreatedAt, CreatedByKid FROM RosterBatches"
$batchById = @{}
foreach ($b in $batches) { $batchById[[int]$b.Id] = $b }

# Every WIC_DUTY ShiftEntries row of the scope period, so the DOUBLE detail can name
# the source of the OTHER agents present at the centre (they are not in scope: they
# already have WicShiftEntries).  Insert order (Id) is the "who was first" evidence.
$seByEmpDate = @{}
foreach ($r in (Invoke-Rows "SELECT Id, EmployeeId, CONVERT(varchar(10), ShiftDate, 23) D, SourceModule, SourceId, AutoGenerated, ManualChangeBy FROM ShiftEntries WHERE ShiftType='WIC_DUTY' AND ShiftDate >= CONVERT(date, '$scopeFrom', 23)")) {
    $k = $r.EmployeeId + '|' + $r.D
    if (-not $seByEmpDate.ContainsKey($k)) { $seByEmpDate[$k] = New-Object System.Collections.ArrayList }
    [void]$seByEmpDate[$k].Add($r)
}

# --- indexes -------------------------------------------------------------
$byKey = @{}   # AgentTask key -> list of locations
foreach ($l in $locs) {
    $keys = New-Object System.Collections.ArrayList
    foreach ($n in @($l.DisplayName, $l.LocationCode, $l.LocationCodeLegacy)) {
        if (-not [string]::IsNullOrWhiteSpace($n)) { [void]$keys.Add($n.Trim().ToLowerInvariant()) }
    }
    if ($l.DisplayName -and $l.DisplayName.Trim().Length -gt 20) {
        [void]$keys.Add($l.DisplayName.Trim().Substring(0,20).ToLowerInvariant())
    }
    foreach ($k in ($keys | Select-Object -Unique)) {
        if (-not $byKey.ContainsKey($k)) { $byKey[$k] = New-Object System.Collections.ArrayList }
        [void]$byKey[$k].Add($l)
    }
}
$empById = @{}
foreach ($e in $emps) { $empById[$e.EmployeeId] = $e }

$assignSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($a in $assign) {
    if ([string]::IsNullOrWhiteSpace($a.EmployeeName) -or [string]::IsNullOrWhiteSpace($a.LocationCode)) { continue }
    [void]$assignSet.Add(($a.EmployeeName.Trim().ToLowerInvariant() + '|' + $a.LocationCode.Trim().ToLowerInvariant()))
}

$hoursByCode = @{}
foreach ($h in $hours) {
    if (-not $hoursByCode.ContainsKey($h.LocationCode)) { $hoursByCode[$h.LocationCode] = New-Object System.Collections.ArrayList }
    [void]$hoursByCode[$h.LocationCode].Add($h)
}
function Resolve-Hours($loc, [string]$ds) {
    $dow = ([datetime]::ParseExact($ds,'yyyy-MM-dd',$null)).DayOfWeek.value__
    $codes = @($loc.LocationCode); if ($loc.LocationCodeLegacy) { $codes += $loc.LocationCodeLegacy }
    $cand = New-Object System.Collections.ArrayList
    foreach ($c in $codes) { if ($hoursByCode.ContainsKey($c)) { foreach ($h in $hoursByCode[$c]) { [void]$cand.Add($h) } } }
    $exc = $cand | Where-Object { $_.ExDate -eq $ds } | Sort-Object { if ($_.MAt) { $_.MAt } else { '' } } -Descending | Select-Object -First 1
    if ($exc) { return $exc }
    return ($cand | Where-Object { -not $_.ExDate -and $_.DayOfWeek -eq $dow -and (-not $_.EffFrom -or $_.EffFrom -le $ds) } |
        Sort-Object { if ($_.EffFrom) { $_.EffFrom } else { '0001-01-01' } } -Descending | Select-Object -First 1)
}

# on-site presence per date+centre (exact SupportLocation == DisplayName, active employee)
$presentBy = @{}
foreach ($w in $wic) {
    if (-not $w.IsOnSite) { continue }
    if ([string]::IsNullOrWhiteSpace($w.SupportLocation)) { continue }
    $emp = $empById[$w.EmployeeId]
    if (-not $emp -or -not $emp.IsActive) { continue }
    $k = "$($w.D)|$($w.SupportLocation.Trim().ToLowerInvariant())"
    if (-not $presentBy.ContainsKey($k)) { $presentBy[$k] = New-Object System.Collections.ArrayList }
    [void]$presentBy[$k].Add($w)
}
$sickByEmp = @{}
foreach ($s in $sick) { if (-not $sickByEmp.ContainsKey($s.EmployeeId)) { $sickByEmp[$s.EmployeeId] = New-Object System.Collections.ArrayList }; [void]$sickByEmp[$s.EmployeeId].Add($s) }
$vacByEmp = @{}
foreach ($v in $vac)  { if (-not $vacByEmp.ContainsKey($v.EmployeeId))  { $vacByEmp[$v.EmployeeId]  = New-Object System.Collections.ArrayList }; [void]$vacByEmp[$v.EmployeeId].Add($v) }
$holByDate = @{}
foreach ($h in $hols) { if (-not $holByDate.ContainsKey($h.D)) { $holByDate[$h.D] = New-Object System.Collections.ArrayList }; [void]$holByDate[$h.D].Add($h) }
# absence ShiftEntries on the same employee+date (AL/SL/PH/OFF/... — canonical disqualifiers)
$absSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($r in (Invoke-Rows "SELECT DISTINCT se.EmployeeId, CONVERT(varchar(10), se.ShiftDate, 23) D, se.ShiftType FROM ShiftEntries se WHERE se.ShiftType IN ('AL','SL','PH','OFF','OFF_WEEKEND','OL','HALF_AL','CD','UL','LPH','RESIGNED','SICK')")) {
    [void]$absSet.Add(($r.EmployeeId + '|' + $r.D + '|' + $r.ShiftType))
}

# ---------------------------------------------------------------------------
# Check (d'): the USUAL SHIFT of every employee in scope
# ---------------------------------------------------------------------------
# Observation = a baseline row with both times.  Usual shift = the most frequent
# ShiftStart-ShiftEnd; ties go to the window seen most recently, then to the
# lexicographically smaller window so the answer is deterministic.
$baseByEmp = @{}
foreach ($b in $baseRows) {
    $win = Normalize-Window $b.ShiftStart $b.ShiftEnd
    if (-not $baseByEmp.ContainsKey($b.EmployeeId)) {
        $baseByEmp[$b.EmployeeId] = [pscustomobject]@{
            EmployeeId   = $b.EmployeeId
            RawRows      = 0
            UsableRows   = 0
            NoTimeRows   = 0
            Windows      = @{}
            ManualRows   = 0
            WorkingRows  = 0
        }
    }
    $st = $baseByEmp[$b.EmployeeId]
    $st.RawRows++
    if ($b.ManualChangeBy) { $st.ManualRows++ }
    if ($b.ShiftType -eq 'WORKING') { $st.WorkingRows++ }
    if (-not $win) { $st.NoTimeRows++; continue }
    $st.UsableRows++
    if (-not $st.Windows.ContainsKey($win)) { $st.Windows[$win] = [pscustomobject]@{ Window=$win; Count=0; LastDate='' } }
    $w = $st.Windows[$win]
    $w.Count++
    if ($b.D -gt $w.LastDate) { $w.LastDate = $b.D }
}

$scopeEmps = @($rows | Select-Object -ExpandProperty EmployeeId -Unique | Sort-Object)
$baselineRows = New-Object System.Collections.ArrayList
foreach ($eid in $scopeEmps) {
    $st = if ($baseByEmp.ContainsKey($eid)) { $baseByEmp[$eid] } else {
        [pscustomobject]@{ EmployeeId=$eid; RawRows=0; UsableRows=0; NoTimeRows=0; Windows=@{}; ManualRows=0; WorkingRows=0 }
    }
    $usual = $null; $usualCount = 0; $usualLast = ''
    if ($st.Windows.Count -gt 0) {
        $ranked = $st.Windows.Values | Sort-Object `
            @{Expression={$_.Count}; Descending=$true}, `
            @{Expression={$_.LastDate}; Descending=$true}, `
            @{Expression={$_.Window}; Descending=$false}
        $top = $ranked | Select-Object -First 1
        $usual = $top.Window; $usualCount = $top.Count; $usualLast = $top.LastDate
    }
    $usable = ($st.UsableRows -ge $MinBaselineRows)
    [void]$baselineRows.Add([pscustomobject]@{
        EmployeeId = $eid
        BaselineRows = $st.UsableRows
        ManualRows = $st.ManualRows
        WorkingRows = $st.WorkingRows
        NoTimeRows = $st.NoTimeRows
        DistinctWindows = $st.Windows.Count
        UsualShift = $usual
        UsualShiftCount = $usualCount
        UsualShiftLastSeen = $usualLast
        BaselineUsable = $usable
        Status = if ($usable) { 'BASELINE_OK' } else { 'NO_BASELINE' }
    })
}

# ---------------------------------------------------------------------------
# Evaluate the checks
# ---------------------------------------------------------------------------
$results = New-Object System.Collections.ArrayList

foreach ($r in $rows) {
    $emp = $empById[$r.EmployeeId]
    $loc = $null
    if (-not [string]::IsNullOrWhiteSpace($r.AgentTask)) {
        $k = $r.AgentTask.Trim().ToLowerInvariant()
        if ($byKey.ContainsKey($k)) {
            $loc = $byKey[$k] | Sort-Object @{Expression={ if ($_.IsActive) {0} else {1} }} | Select-Object -First 1
        }
    }

    $failA = New-Object System.Collections.ArrayList
    $failB = New-Object System.Collections.ArrayList
    $failC = New-Object System.Collections.ArrayList
    $flags = New-Object System.Collections.ArrayList

    # ---- a) active + absent ----
    if (-not $emp) { [void]$failA.Add("A_NO_EMPLOYEE") }
    elseif (-not $emp.IsActive) { [void]$failA.Add("A_INACTIVE") }
    if ($emp) {
        if ($sickByEmp.ContainsKey($r.EmployeeId)) {
            foreach ($s in $sickByEmp[$r.EmployeeId]) {
                if ($s.F -le $r.D -and $s.L -ge $r.D) { [void]$failA.Add("A_SICK"); break }
            }
        }
        if ($vacByEmp.ContainsKey($r.EmployeeId)) {
            foreach ($v in $vacByEmp[$r.EmployeeId]) {
                if ($v.F -le $r.D -and $v.L -ge $r.D) { [void]$failA.Add("A_AL"); break }
            }
        }
        foreach ($t in @('AL','SL','PH','OFF','OFF_WEEKEND','SICK','RESIGNED')) {
            if ($absSet.Contains(($r.EmployeeId + '|' + $r.D + '|' + $t))) { [void]$failA.Add("A_SHIFTENTRY_$t") }
        }
    }
    if ($holByDate.ContainsKey($r.D)) {
        $empBL = if ($emp) { $emp.Bundesland } else { $null }
        $locBL = if ($loc) { $loc.Bundesland } else { $null }
        foreach ($h in $holByDate[$r.D]) {
            if ($h.IsNational) { [void]$failA.Add("A_HOLIDAY_NATIONAL"); break }
            if ((Test-BundeslandMatch $h.Bundesland $empBL) -or (Test-BundeslandMatch $h.Bundesland $locBL)) {
                [void]$failA.Add("A_HOLIDAY_REGIONAL"); break
            }
        }
    }

    # ---- b) centre open ----
    $hRow = $null; $duty = $null
    if (-not $loc) { [void]$failB.Add("B_NO_LOCATION") }
    else {
        $hRow = Resolve-Hours $loc $r.D
        $duty = Get-DutyWindow $hRow
        if (-not $hRow) { [void]$failB.Add("B_NO_HOURS_ROW") }
        elseif ($hRow.IsClosed) { [void]$failB.Add("B_CLOSED") }
        elseif (-not $duty) { [void]$failB.Add("B_NO_WINDOW") }
    }

    # ---- c) MAIN/BACKUP/REGIONAL ----
    $assignTypes = @()
    if ($emp -and $loc) {
        $nameKey = $emp.FullName.Trim().ToLowerInvariant()
        foreach ($code in @($loc.LocationCode, $loc.LocationCodeLegacy)) {
            if ([string]::IsNullOrWhiteSpace($code)) { continue }
            if ($assignSet.Contains(($nameKey + '|' + $code.Trim().ToLowerInvariant()))) { $assignTypes += $code }
        }
    }
    if ($assignTypes.Count -eq 0) { [void]$failC.Add("C_NO_ASSIGNMENT") }

    # ---- d') usual shift of THIS employee ----
    $rowWin = Normalize-Window $r.ShiftStart $r.ShiftEnd
    $base = $baselineRows | Where-Object { $_.EmployeeId -eq $r.EmployeeId } | Select-Object -First 1
    $dStatus = 'D_OK'
    $centreHoursFlag = $false
    if (-not $base -or -not $base.BaselineUsable) {
        $dStatus = 'NO_BASELINE'
        [void]$flags.Add("NO_BASELINE")
    }
    elseif (-not $rowWin -or $rowWin -ne $base.UsualShift) {
        $dStatus = 'SHIFT_MISMATCH'
        [void]$flags.Add("SHIFT_MISMATCH")
    }
    # Was the row simply the centre's own window (the writer-#1 "batch with no
    # time -> centre working hours" shape)?  Reported, no longer a verdict.
    if ($hRow -and -not $hRow.IsClosed -and $duty -and $rowWin -and $rowWin -eq $duty) {
        $centreHoursFlag = $true
        [void]$flags.Add("CENTRE_HOURS")
    }

    # ---- e) DOUBLE ----
    $present = 0; $minReq = $null; $double = $false; $evaluated = $false
    $presentIds = @()
    if ($loc) {
        $pk = "$($r.D)|$($loc.DisplayName.Trim().ToLowerInvariant())"
        if ($presentBy.ContainsKey($pk)) {
            $present = $presentBy[$pk].Count
            $presentIds = @($presentBy[$pk] | Sort-Object Id | ForEach-Object { $_.EmployeeId })
        }
        $minReq = if ($hRow -and $hRow.MinRequired) { [int]$hRow.MinRequired }
                  elseif ($loc.MinAgentsRequired) { [int]$loc.MinAgentsRequired } else { 1 }
        if ($present -ge 1) {
            $evaluated = $true
            if (($present + 1) -gt $minReq) { $double = $true; [void]$flags.Add("DOUBLE") }
        }
    }

    $passA = ($failA.Count -eq 0)
    $passB = ($failB.Count -eq 0)
    $passC = ($failC.Count -eq 0)
    $passD = ($dStatus -eq 'D_OK')
    $passE = -not $double

    # The backfill set: a, b, c pass and NOT DOUBLE.  A SHIFT_MISMATCH or
    # NO_BASELINE row is deliberately still in the plan.
    $inPlan = $passA -and $passB -and $passC -and $passE
    $planShift = $null
    $planSkip  = ''
    if ($inPlan) {
        $planShift = Get-IntersectionWindow $rowWin $duty
        if (-not $planShift) { $planSkip = 'PLAN_NO_OVERLAP' }
    }

    $firstFail = if (-not $passA) { ($failA -join ',') }
                 elseif (-not $passB) { ($failB -join ',') }
                 elseif (-not $passC) { ($failC -join ',') }
                 elseif (-not $passE) { 'DOUBLE' }
                 elseif ($dStatus -eq 'SHIFT_MISMATCH') { 'SHIFT_MISMATCH' }
                 elseif ($dStatus -eq 'NO_BASELINE') { 'NO_BASELINE' } else { '' }

    # Row provenance (the writer that put the WIC_DUTY row here).
    $src = if ($r.ManualChangeBy) { 'manual' }
           elseif ($r.SourceModule -eq 'Roster') { 'batch' }
           elseif ($r.SourceModule) { $r.SourceModule } else { 'unknown' }
    $batch = if ($r.SourceId -and $batchById.ContainsKey([int]$r.SourceId)) { $batchById[[int]$r.SourceId] } else { $null }

    [void]$results.Add([pscustomobject]@{
        Id             = $r.Id
        EmployeeId     = $r.EmployeeId
        Date           = $r.D
        Month          = $r.D.Substring(0,7)
        Dow            = $r.Dow
        AgentTask      = $r.AgentTask
        Centre         = if ($loc) { $loc.DisplayName } else { $null }
        LocationCode   = if ($loc) { $loc.LocationCode } else { $null }
        RowShift       = if ($rowWin) { $rowWin } else { "$($r.ShiftStart)-$($r.ShiftEnd)".Trim('-') }
        CentreWindow   = $duty
        UsualShift     = if ($base) { $base.UsualShift } else { $null }
        BaselineRows   = if ($base) { $base.BaselineRows } else { 0 }
        DStatus        = $dStatus
        PassA          = $passA
        PassB          = $passB
        PassC          = $passC
        PassD          = $passD
        PassE          = $passE
        FailA          = ($failA -join ',')
        FailB          = ($failB -join ',')
        FailC          = ($failC -join ',')
        Flags          = ($flags -join ',')
        FirstFail      = $firstFail
        InPlan         = $inPlan
        PlanWorkingShift = $planShift
        PlanSkip       = $planSkip
        PresentAtCentre= $present
        PresentIds     = ($presentIds -join ',')
        MinRequired    = $minReq
        DoubleEvaluated= $evaluated
        AssignmentVia  = ($assignTypes -join ',')
        SourceId       = $r.SourceId
        SourceKind     = $src
        BatchCreatedAt = if ($batch) { $batch.CreatedAt } else { $null }
        BatchCreatedBy = if ($batch) { $batch.CreatedByKid } else { $null }
        Auto           = $r.AutoGenerated
        ManualBy       = $r.ManualChangeBy
        Candidate      = $inPlan
    })
}

$planRows = @($results | Where-Object { $_.InPlan })
$planWritable = @($planRows | Where-Object { $_.PlanWorkingShift })
$mismatchRows = @($results | Where-Object { $_.DStatus -eq 'SHIFT_MISMATCH' })
$noBaseRows   = @($results | Where-Object { $_.DStatus -eq 'NO_BASELINE' })
$doubleRows   = @($results | Where-Object { $_.Flags -like '*DOUBLE*' })

# ---------------------------------------------------------------------------
# Reports
# ---------------------------------------------------------------------------
$out = New-Object System.Collections.ArrayList
function P([string]$s = "") { [void]$out.Add($s) }

P "WIC_DUTY -> WicShiftEntries BACKFILL CANDIDATE ANALYSIS (check d replaced by d')"
P "mode            : $mode"
P "server now      : $serverToday   scope: ShiftDate >= $scopeFrom"
P "baseline window : $baselineFrom .. $serverToday ($BaselineWeeks weeks, manual + WORKING rows)"
P "NO_BASELINE when: fewer than $MinBaselineRows usable baseline rows"
P "task3 rows      : $($rows.Count)  over $($scopeEmps.Count) employee(s)"
P "backfill set    : $($planRows.Count)   (pass a+b+c and NOT DOUBLE; SHIFT_MISMATCH/NO_BASELINE INCLUDED)"
P "  of those, plan has a usable WorkingShift : $($planWritable.Count)"
P "  of those, PLAN_NO_OVERLAP (shift outside centre hours) : $(@($planRows | Where-Object { $_.PlanSkip }).Count)"
P "SHIFT_MISMATCH  : $($mismatchRows.Count)"
P "NO_BASELINE     : $($noBaseRows.Count)"
P "DOUBLE          : $($doubleRows.Count)"
P "CENTRE_HOURS    : $(@($results | Where-Object { $_.Flags -like '*CENTRE_HOURS*' }).Count)  (row time == centre window; reported only)"
P ""
P "=== 1) PER MONTH: backfill set / SHIFT_MISMATCH / NO_BASELINE / DOUBLE ==="
$hdr = "{0,-9} {1,6} {2,8} {3,15} {4,12} {5,7} {6,7} {7,7}"
P ($hdr -f "month","total","PLAN","SHIFT_MISMATCH","NO_BASELINE","DOUBLE","d_ok","c_hours")
P ("-"*9 + " " + ("-"*6) + " " + ("-"*8) + " " + ("-"*15) + " " + ("-"*12) + " " + ("-"*23))
$months = $results | Group-Object Month | Sort-Object Name
foreach ($g in $months) {
    $m = $g.Group
    P ($hdr -f $g.Name, $m.Count,
        @($m | Where-Object { $_.InPlan }).Count,
        @($m | Where-Object { $_.DStatus -eq 'SHIFT_MISMATCH' }).Count,
        @($m | Where-Object { $_.DStatus -eq 'NO_BASELINE' }).Count,
        @($m | Where-Object { $_.Flags -like '*DOUBLE*' }).Count,
        @($m | Where-Object { $_.DStatus -eq 'D_OK' }).Count,
        @($m | Where-Object { $_.Flags -like '*CENTRE_HOURS*' }).Count)
}
P ($hdr -f "TOTAL", $results.Count, $planRows.Count, $mismatchRows.Count, $noBaseRows.Count,
    $doubleRows.Count,
    @($results | Where-Object { $_.DStatus -eq 'D_OK' }).Count,
    @($results | Where-Object { $_.Flags -like '*CENTRE_HOURS*' }).Count)
P ""
P "  Note: SHIFT_MISMATCH / NO_BASELINE are verdicts of check d' and are INSIDE the plan;"
P "        DOUBLE is check e and is the only verdict that keeps a row out of the plan."
P "        Columns overlap: a DOUBLE row can also be SHIFT_MISMATCH/NO_BASELINE."
P ""
P "=== 1b) USUAL SHIFT per employee (the 36 in scope) ==="
P ("{0,-9} {1,7} {2,7} {3,7} {4,6} {5,-13} {6,6} {7,-10}  {8}" -f `
  "emp","base","manual","work","nWin","usual","count","lastSeen","status")
foreach ($b in $baselineRows) {
    P ("{0,-9} {1,7} {2,7} {3,7} {4,6} {5,-13} {6,6} {7,-10}  {8}" -f `
      $b.EmployeeId, $b.BaselineRows, $b.ManualRows, $b.WorkingRows, $b.DistinctWindows,
      $(if ($b.UsualShift) { $b.UsualShift } else { '-' }), $b.UsualShiftCount,
      $(if ($b.UsualShiftLastSeen) { $b.UsualShiftLastSeen } else { '-' }), $b.Status)
}
P ""
P "=== 1c) SHIFT_MISMATCH examples (row shift <> that employee's usual shift) ==="
P ("{0,-6} {1,-9} {2,-10} {3,-26} {4,-13} {5,-13} {6,-5} {7}" -f `
  "seId","emp","date","centre","rowShift","usualShift","nBase","source")
foreach ($x in ($mismatchRows | Sort-Object Date, EmployeeId | Select-Object -First 15)) {
    P ("{0,-6} {1,-9} {2,-10} {3,-26} {4,-13} {5,-13} {6,-5} {7}" -f `
      $x.Id, $x.EmployeeId, $x.Date, $x.Centre, $x.RowShift, $x.UsualShift, $x.BaselineRows, $x.SourceKind)
}
if ($mismatchRows.Count -gt 15) { P ("  ... and {0} more (full list in the CSV)" -f ($mismatchRows.Count - 15)) }
P ""
P "=== 1d) NO_BASELINE examples (employee has no usual shift yet) ==="
P ("{0,-6} {1,-9} {2,-10} {3,-26} {4,-13} {5,-5} {6}" -f "seId","emp","date","centre","rowShift","nBase","source")
foreach ($x in ($noBaseRows | Sort-Object Date, EmployeeId | Select-Object -First 10)) {
    P ("{0,-6} {1,-9} {2,-10} {3,-26} {4,-13} {5,-5} {6}" -f `
      $x.Id, $x.EmployeeId, $x.Date, $x.Centre, $x.RowShift, $x.BaselineRows, $x.SourceKind)
}
if ($noBaseRows.Count -gt 10) { P ("  ... and {0} more (full list in the CSV)" -f ($noBaseRows.Count - 10)) }

P ""
P "=== 2) BACKFILL VISIBILITY PLAN (dry run): WicShiftEntries rows ==="
P "WorkingShift = ShiftEntries shift CUT TO the centre window (SyncWicDutyFromShiftPlanAsync shape), IsOnSite=1, Task='WIC'."
P "ShiftEntries is never changed."
P ("{0,-9} {1,7}" -f "month","rows")
$planMonths = $planWritable | Group-Object Month | Sort-Object Name
foreach ($g in $planMonths) { P ("{0,-9} {1,7}" -f $g.Name, $g.Count) }
P ("{0,-9} {1,7}" -f "TOTAL", $planWritable.Count)
P ("  rows in the set without a usable WorkingShift (PLAN_NO_OVERLAP): {0}" -f @($planRows | Where-Object { $_.PlanSkip }).Count)
P ""
P " 5 examples:"
P ("{0,-6} {1,-9} {2,-10} {3,-26} {4,-12} {5,-12} {6,-13} {7}" -f `
  "seId","emp","date","SupportLocation","rowShift","centreWin","WorkingShift","d'")
foreach ($x in ($planWritable | Sort-Object Date, EmployeeId | Select-Object -First 5)) {
    P ("{0,-6} {1,-9} {2,-10} {3,-26} {4,-12} {5,-12} {6,-13} {7}" -f `
      $x.Id, $x.EmployeeId, $x.Date, $x.Centre, $x.RowShift, $x.CentreWindow, $x.PlanWorkingShift, $x.DStatus)
}

P ""
P "=== 3) DOUBLE rows (check e): both employees, both sources, who was first ==="
P ("{0,-10} {1,-26} {2,-9} {3,-9} {4,-13} {5,-10} {6,-8} {7,-20}" -f `
  "date","centre","candEmp","firstEmp","existingEmpIds","minReq","present","candSource")
foreach ($x in ($doubleRows | Sort-Object Date)) {
    P ("{0,-10} {1,-26} {2,-9} {3,-9} {4,-13} {5,-10} {6,-8} {7,-20}" -f `
      $x.Date, $x.Centre, $x.EmployeeId, @($x.PresentIds -split ',')[0], $x.PresentIds, $x.MinRequired,
      $x.PresentAtCentre, ($x.SourceKind + $(if ($x.BatchCreatedAt) { " ($($x.BatchCreatedBy) $($x.BatchCreatedAt))" } else { "" })))
}
P ""
P "  Full per-day detail of every DOUBLE day is in the DOUBLE DETAIL section below."

P ""
P "=== 3b) DOUBLE DETAIL: every agent on the centre/date, his source and insert order ==="
P "role: candidate = the in-scope WIC_DUTY row with NO WicShiftEntries; present = on-site WicShiftEntries already there."
P "first = lowest ShiftEntries.Id among that day's WIC_DUTY rows (the row written first)."
P ("{0,-10} {1,-26} {2,-9} {3,-9} {4,-6} {5,-18} {6,-8} {7,-14} {8}" -f `
  "date","centre","emp","role","seId","source","wicId","workingShift","first")
foreach ($x in ($doubleRows | Sort-Object Date, Centre)) {
    $dayRows = New-Object System.Collections.ArrayList
    foreach ($eid in (@($x.EmployeeId) + @($x.PresentIds -split ',' | Where-Object { $_ }))) {
        $seList = if ($seByEmpDate.ContainsKey("$eid|$($x.Date)")) { $seByEmpDate["$eid|$($x.Date)"] } else { @() }
        foreach ($se in $seList) {
            [void]$dayRows.Add([pscustomobject]@{ EmployeeId=$eid; SeId=[int]$se.Id; Source=$(if ($se.ManualChangeBy) { 'manual' } elseif ($se.SourceModule -eq 'Roster') { 'batch' } elseif ($se.SourceModule) { $se.SourceModule } else { 'unknown' }); Auto=[bool]$se.AutoGenerated; Batch = if ($se.SourceId -and $batchById.ContainsKey([int]$se.SourceId)) { $batchById[[int]$se.SourceId] } else { $null } })
        }
    }
    $ordered = @($dayRows | Sort-Object SeId)
    $firstId = if ($ordered.Count -gt 0) { $ordered[0].SeId } else { $null }
    foreach ($d in $ordered) {
        $pk = "$($x.Date)|$($x.Centre.Trim().ToLowerInvariant())"
        $wicIds = @($presentBy[$pk] | Where-Object { $_.EmployeeId -eq $d.EmployeeId } | ForEach-Object { $_.Id })
        $ws = @($presentBy[$pk] | Where-Object { $_.EmployeeId -eq $d.EmployeeId } | ForEach-Object { $_.WorkingShift })
        $srcTxt = $d.Source
        if ($d.Batch) { $srcTxt = "$($d.Source) [$($d.Batch.CreatedByKid) $($d.Batch.CreatedAt)]" }
        P ("{0,-10} {1,-26} {2,-9} {3,-9} {4,-6} {5,-18} {6,-8} {7,-14} {8}" -f `
          $x.Date, $x.Centre, $d.EmployeeId,
          $(if ($d.EmployeeId -eq $x.EmployeeId) { 'candidate' } else { 'present' }),
          $d.SeId, $srcTxt, (($wicIds -join ',')), (($ws -join ',')),
          $(if ($d.SeId -eq $firstId) { 'FIRST' } else { '' }))
    }
    P ""
}

P ""
P "=== /api/wic/coverage cross-check (centre already covers another agent) ==="
P ("rows with PresentAtCentre >= 1 (endpoint isCovered=True shape) : " + @($results | Where-Object { $_.PresentAtCentre -ge 1 }).Count)
P ("  of those, adding would exceed minRequired (DOUBLE)            : " + $doubleRows.Count)
P ("  of those, covered but still below minRequired (not DOUBLE)   : " + @($results | Where-Object { $_.PresentAtCentre -ge 1 -and $_.Flags -notlike '*DOUBLE*' }).Count)

P ""
P "=== WRITER #1 (batch with no time -> centre working hours) ==="
P "Being fixed in this task: a WIC batch WITHOUT an explicit shift time is now rejected in"
P "RosterService.ValidateAsync and centre hours are never copied into ShiftEntries.ShiftStart/ShiftEnd."
P "  Backend/RosterService.cs:763-764  rowStart/rowEnd = input.Start ?? segments[0].Start/segments[-1].End"
P "  Backend/RosterService.cs:918-929  SegmentFor(): input.Start==null -> new WicSegment(loc, centreStart, centreEnd)"
P "  Backend/RosterService.cs:1091-1092 ApplyGenerated(): e.ShiftStart = row.Start; e.ShiftEnd = row.End"
P ("Rows in scope whose ShiftEntries time equals the centre window (the old shape): " + @($results | Where-Object { $_.Flags -like '*CENTRE_HOURS*' }).Count)

# --- write artefacts ---
$results | Sort-Object Date, EmployeeId |
    Select-Object Id,EmployeeId,Date,Month,Dow,Centre,LocationCode,RowShift,CentreWindow,
        UsualShift,BaselineRows,DStatus,PassA,PassB,PassC,PassD,PassE,FailA,FailB,FailC,Flags,FirstFail,
        InPlan,PlanWorkingShift,PlanSkip,PresentAtCentre,PresentIds,MinRequired,DoubleEvaluated,
        AssignmentVia,SourceId,SourceKind,BatchCreatedAt,BatchCreatedBy,Auto,ManualBy,Candidate |
    Export-Csv -Path $ChecksCsv -NoTypeInformation -Encoding UTF8

$reportText = ($out -join "`r`n")
$reportText | Set-Content -Path $ReportFile -Encoding UTF8

$dry = New-Object System.Collections.ArrayList
[void]$dry.Add("WIC_DUTY backfill - DRY RUN (no writes). Generated $([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss'))")
[void]$dry.Add("scope: ShiftType='WIC_DUTY', ShiftDate >= $scopeFrom, no WicShiftEntries for EmployeeId+ShiftDate")
[void]$dry.Add("mode : $mode")
[void]$dry.Add("set  : pass a+b+c and NOT DOUBLE (SHIFT_MISMATCH and NO_BASELINE are INCLUDED)")
[void]$dry.Add("WorkingShift = ShiftEntries shift cut to the centre window. ShiftEntries is never changed.")
[void]$dry.Add("")
[void]$dry.Add("rows in the plan: $($planWritable.Count)   (PLAN_NO_OVERLAP skipped: $(@($planRows | Where-Object { $_.PlanSkip }).Count))")
[void]$dry.Add("")
[void]$dry.Add(("{0,-6} {1,-9} {2,-10} {3,-26} {4,-12} {5,-13} {6}" -f "seId","emp","date","SupportLocation","rowShift","WorkingShift","d'"))
foreach ($c in ($planWritable | Sort-Object Date, EmployeeId)) {
    [void]$dry.Add(("{0,-6} {1,-9} {2,-10} {3,-26} {4,-12} {5,-13} {6}" -f $c.Id, $c.EmployeeId, $c.Date, $c.Centre, $c.RowShift, $c.PlanWorkingShift, $c.DStatus))
}
[void]$dry.Add("")
[void]$dry.Add("NOTE: this file is a plan only. Nothing was written to the database.")
($dry -join "`r`n") | Set-Content -Path $DryRunFile -Encoding UTF8

# ---------------------------------------------------------------------------
# Apply (only with -Apply)
# ---------------------------------------------------------------------------
$written = 0
if (-not $isDryRun) {
    if ($planWritable.Count -eq 0) { throw "APPLY REFUSED: no candidate rows." }
    if ($ExpectedInsertCount -gt 0 -and $planWritable.Count -ne $ExpectedInsertCount) {
        throw ("APPLY REFUSED: plan has {0} candidate(s), expected {1}." -f $planWritable.Count, $ExpectedInsertCount)
    }
    $countBefore = [int](Invoke-Scalar "SELECT COUNT(*) FROM WicShiftEntries")
    $inserted = New-Object System.Collections.ArrayList
    $tx = $conn.BeginTransaction()
    try {
        foreach ($c in ($planWritable | Sort-Object Date, EmployeeId)) {
            $cmd = $conn.CreateCommand(); $cmd.Transaction = $tx
            $cmd.CommandText = @"
INSERT INTO WicShiftEntries
  (EmployeeId, ShiftDate, DayOfWeek, SupportLocation, LocationCode, IsOnSite, IsGSDDay, IsOffDay, WorkingShift, Task)
OUTPUT INSERTED.Id
VALUES (@emp, CONVERT(date, @d, 23), @dow, @loc, @code, 1, 0, 0, @ws, 'WIC')
"@
            foreach ($pair in @{ "@emp"=$c.EmployeeId; "@d"=$c.Date; "@dow"=[string]$c.Dow; "@loc"=$c.Centre; "@code"=$c.LocationCode; "@ws"=$c.PlanWorkingShift }.GetEnumerator()) {
                $p = $cmd.CreateParameter(); $p.ParameterName = $pair.Key; $p.Value = $pair.Value
                [void]$cmd.Parameters.Add($p)
            }
            $newId = [int]$cmd.ExecuteScalar()
            [void]$inserted.Add([pscustomobject]@{ NewId=$newId; SeId=$c.Id; EmployeeId=$c.EmployeeId; Date=$c.Date; Centre=$c.Centre; WorkingShift=$c.PlanWorkingShift })
            $written++
        }
        $tx.Commit()
    } catch { $tx.Rollback(); throw }
    $countAfter = [int](Invoke-Scalar "SELECT COUNT(*) FROM WicShiftEntries")
    if (-not [string]::IsNullOrWhiteSpace($InsertedIdsFile)) {
        ($inserted | ForEach-Object { "$($_.NewId)`t$($_.SeId)`t$($_.EmployeeId)`t$($_.Date)`t$($_.Centre)`t$($_.WorkingShift)" }) |
            Set-Content -Path $InsertedIdsFile -Encoding UTF8
    }
    if (-not [string]::IsNullOrWhiteSpace($RollbackSqlFile)) {
        $sql = New-Object System.Collections.ArrayList
        [void]$sql.Add("-- rollback of WicShiftEntries inserted by Backfill-WicDutyCandidates.ps1 -Apply")
        [void]$sql.Add("DELETE w FROM WicShiftEntries w WHERE w.Id IN ($(($inserted.NewId) -join ','));")
        ($sql -join "`r`n") | Set-Content -Path $RollbackSqlFile -Encoding UTF8
    }
    P ""
    P "=== APPLY RESULT ==="
    P ("inserted {0} row(s); WicShiftEntries {1} -> {2}" -f $written, $countBefore, $countAfter)
    ($out -join "`r`n") | Set-Content -Path $ReportFile -Encoding UTF8
}

$conn.Close()

$summary = ($out | Select-Object -First 40) -join "`r`n"
Write-Output $summary
Write-Output ""
Write-Output "checks csv : $ChecksCsv"
Write-Output "report     : $ReportFile"
Write-Output "dry run    : $DryRunFile"
if ($isDryRun) { Write-Output "NO WRITES PERFORMED." }
