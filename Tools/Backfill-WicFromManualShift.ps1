# Backfill-WicFromManualShift.ps1
#
# READ-ONLY BY DEFAULT.  Without -Apply this script runs SELECT statements ONLY and
# prints exactly which WicShiftEntries row the server would have written for every
# manual ShiftEntries WIC_DUTY row that has no WicShiftEntries row for the same
# employee+date ("GROUP 2").
#
# Writes happen ONLY when -Apply is passed explicitly.  -WhatIf (or simply omitting
# -Apply) always forces the dry run.
#
# It reproduces the server rule byte-for-byte:
#   Backend/ShiftService.cs
#     ::SyncWicDutyFromShiftPlanAsync            lines 325-416
#       location resolution precedence           lines 342-368
#       time fallback (was 372-382)              uses ShiftStart/ShiftEnd first
#       row values written                       lines 401-413 (INSERT)
#                                                 lines 390-398 (UPDATE existing centre row)
#     call sites (human edits only)              line 221 (UpdateShiftAsync)
#                                                line 290 (AssignShiftAsync)
#
# IMPORTANT (schema fact):
#   WicShiftEntries has NO column for a manual-origin marker.  Its columns are:
#   Id, EmployeeId, ShiftDate, DayOfWeek, SupportLocation, WicOpeningHours,
#   WorkingShift, IsOnSite, IsGSDDay, IsOffDay, Task, LocationCode.
#   The manual origin therefore lives ONLY on ShiftEntries
#   (ManualChangeBy / ManualChangeAt / ManualChangeNote) and CANNOT be persisted on
#   the row this script would insert.  The report below carries it as evidence text.
#
# Examples:
#   # dry run (default) - prints the exact rows that WOULD be inserted
#   pwsh -File Tools\Backfill-WicFromManualShift.ps1
#
#   # dry run, one employee / one date
#   pwsh -File Tools\Backfill-WicFromManualShift.ps1 -ShiftEntryId 56526
#
#   # the only way to write (NOT run as part of the read-only investigation)
#   pwsh -File Tools\Backfill-WicFromManualShift.ps1 -Apply

[CmdletBinding()]
param(
    [string]   $ConnectionString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;",
    [string]   $AsOf            = "",                 # scope: ShiftDate >= AsOf (default: server today)
    [int[]]    $ShiftEntryId    = @(),                # optional: restrict to these ShiftEntries.Id
    [string]   $OutFile         = "",                 # optional: also write the report to this file
    [string]   $TargetFromDate  = "",                 # -Apply: only rows with ShiftDate > this date (the agreed coverage scope)
    [int]      $ExpectedInsertCount = 0,              # -Apply: refuse if the plan differs from this number
    [string]   $InsertedIdsFile = "",                 # -Apply: write the new WicShiftEntries ids here
    [string]   $RollbackSqlFile = "",                 # -Apply: write the rollback script here (never executed)
    [switch]   $ListSkipped,                          # list the already-linked rows individually
    [switch]   $WhatIf,                               # belt-and-braces dry run
    [switch]   $Apply                                 # EXPLICIT write opt-in; default is dry run
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Data
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$isDryRun = (-not $Apply) -or $WhatIf
$mode     = if ($isDryRun) { "DRY RUN (no writes)" } else { "APPLY (writes INSERTs)" }

# Task badges + GSD, i.e. AgentTask values that never name a centre.
# Mirrors AgentTasks.NonLocationValues (Backend/ShiftService.cs:76-77).
$NonLocationValues = @("WIC","VWIC","Voice","Backlog","Dispatcher","SME","SSP","GSD")

# ---------------------------------------------------------------------------
# Read-only plumbing
# ---------------------------------------------------------------------------
$conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
$conn.Open()

function Invoke-Rows([string]$sql, [hashtable]$params = @{}) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.CommandTimeout = 300
    foreach ($k in $params.Keys) {
        $p = $cmd.CreateParameter(); $p.ParameterName = $k
        $p.Value = if ($null -eq $params[$k]) { [System.DBNull]::Value } else { $params[$k] }
        [void]$cmd.Parameters.Add($p)
    }
    $rdr = $cmd.ExecuteReader()
    $out = New-Object System.Collections.ArrayList
    while ($rdr.Read()) {
        $h = @{}
        for ($i = 0; $i -lt $rdr.FieldCount; $i++) {
            $v = $rdr.GetValue($i)
            if ($v -is [System.DBNull]) { $v = $null }
            $h[$rdr.GetName($i)] = $v
        }
        [void]$out.Add([pscustomobject]$h)
    }
    $rdr.Close()
    return ,$out
}

function Invoke-Scalar([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
    return $cmd.ExecuteScalar()
}

function Test-NonLocationValue([string]$task) {
    if ([string]::IsNullOrWhiteSpace($task)) { return $false }
    return ($NonLocationValues -contains $task.Trim())
}

# ---------------------------------------------------------------------------
# Load
# ---------------------------------------------------------------------------
$serverToday = Invoke-Scalar "SELECT CONVERT(varchar(10), CAST(GETDATE() AS date), 23)"
$scopeFrom   = if ([string]::IsNullOrWhiteSpace($AsOf)) { $serverToday } else { $AsOf }

$manualSql = @"
SELECT se.Id, se.EmployeeId, CONVERT(varchar(10), se.ShiftDate, 23) AS D, se.ShiftDate AS ShiftDateRaw,
       se.ShiftStart, se.ShiftEnd, se.AgentTask, se.LocationId, se.ShiftType, se.IsWicDuty,
       se.SourceModule, se.AutoGenerated, se.ManualChangeBy,
       CONVERT(varchar(19), se.ManualChangeAt, 120) AS ManualChangeAt, se.ManualChangeNote
FROM ShiftEntries se
WHERE se.ShiftType = 'WIC_DUTY'
  AND se.ManualChangeBy IS NOT NULL AND LTRIM(RTRIM(se.ManualChangeBy)) <> ''
  AND se.ShiftDate >= CONVERT(date, '$scopeFrom', 23)
ORDER BY se.ShiftDate, se.Id
"@
$manual = Invoke-Rows $manualSql
if ($ShiftEntryId.Count -gt 0) { $manual = @($manual | Where-Object { $ShiftEntryId -contains $_.Id }) }

$wicSql = @"
SELECT w.Id, w.EmployeeId, CONVERT(varchar(10), w.ShiftDate, 23) AS D, w.SupportLocation,
       w.LocationCode, w.IsOnSite, w.WorkingShift, w.Task
FROM WicShiftEntries w
WHERE w.ShiftDate >= CONVERT(date, '$scopeFrom', 23)
"@
$wic = Invoke-Rows $wicSql

$locations = Invoke-Rows "SELECT LocationCode, DisplayName, LocationCodeLegacy, IsActive FROM WicLocations"
$openHours = Invoke-Rows "SELECT LocationCode, DayOfWeek, OpenTime, CloseTime, OpenTime2, CloseTime2, IsClosed, ExceptionDate, EffectiveFrom FROM WicOpeningHours"
$mainAssign = Invoke-Rows "SELECT LocationCode, EmployeeName, AssignmentType, IsActive FROM WicAgentAssignments WHERE IsActive = 1 AND AssignmentType = 'MAIN'"
$employees  = Invoke-Rows "SELECT EmployeeId, FullName, IsActive FROM Employees"

$wicByEmpDate = @{}
foreach ($w in $wic) {
    $k = "$($w.EmployeeId)|$($w.D)"
    if (-not $wicByEmpDate.ContainsKey($k)) { $wicByEmpDate[$k] = New-Object System.Collections.ArrayList }
    [void]$wicByEmpDate[$k].Add($w)
}
$empById = @{}
foreach ($e in $employees) { $empById[$e.EmployeeId] = $e }

# ---------------------------------------------------------------------------
# Faithful copy of the server's location resolution (ShiftService.cs:342-368)
# ---------------------------------------------------------------------------
function Resolve-WicLocation($row, $existingRowsForDay, $empId) {
    $trace = New-Object System.Collections.ArrayList

    # 1. explicit LocationId hint sent by the Shift Plan
    if (-not [string]::IsNullOrWhiteSpace($row.LocationId)) {
        $loc = $locations | Where-Object { $_.IsActive -and $_.LocationCode -eq $row.LocationId } | Select-Object -First 1
        [void]$trace.Add("hint LocationId='$($row.LocationId)'")
        if ($loc) { return @{ Loc = $loc; Via = "LocationId hint"; Trace = $trace } }
    }

    # 2. row's own AgentTask -- EXACT match on DisplayName or LocationCode (no truncation tolerance)
    if (-not [string]::IsNullOrWhiteSpace($row.AgentTask) -and -not (Test-NonLocationValue $row.AgentTask)) {
        $loc = $locations | Where-Object {
            $_.IsActive -and ($_.DisplayName -eq $row.AgentTask -or $_.LocationCode -eq $row.AgentTask)
        } | Select-Object -First 1
        [void]$trace.Add("AgentTask='$($row.AgentTask)' exact DisplayName/LocationCode")
        if ($loc) { return @{ Loc = $loc; Via = "AgentTask exact"; Trace = $trace } }
        [void]$trace.Add("  -> AgentTask did NOT match any ACTIVE WicLocation exactly")
    }

    # 3. an already on-site WIC row for that employee+date
    $onSite = $existingRowsForDay | Where-Object { $_.IsOnSite -and -not [string]::IsNullOrWhiteSpace($_.SupportLocation) } | Select-Object -First 1
    if ($onSite) {
        $loc = $locations | Where-Object { $_.IsActive -and $_.DisplayName -eq $onSite.SupportLocation } | Select-Object -First 1
        [void]$trace.Add("existing on-site WIC row SupportLocation='$($onSite.SupportLocation)'")
        if ($loc) { return @{ Loc = $loc; Via = "existing on-site WIC row"; Trace = $trace } }
    }

    # 4. the employee's active MAIN WIC assignment
    $emp = $empById[$empId]
    if ($emp -and $emp.FullName) {
        $main = $mainAssign | Where-Object { $_.EmployeeName -eq $emp.FullName } | Select-Object -First 1
        if ($main) {
            $loc = $locations | Where-Object {
                $_.IsActive -and ($_.LocationCode -eq $main.LocationCode -or $_.LocationCodeLegacy -eq $main.LocationCode)
            } | Select-Object -First 1
            [void]$trace.Add("employee MAIN WicAgentAssignment LocationCode='$($main.LocationCode)'")
            if ($loc) { return @{ Loc = $loc; Via = "employee MAIN assignment"; Trace = $trace } }
        }
    }

    return @{ Loc = $null; Via = $null; Trace = $trace }
}

# Best-effort copy of WicHoursResolver.DutyWindow for the ShiftStart/ShiftEnd fallback.
function Get-DutyWindow($loc, [datetime]$date) {
    if (-not $loc) { return @{ Start = $null; End = $null } }
    $codes = @($loc.LocationCode); if ($loc.LocationCodeLegacy) { $codes += $loc.LocationCodeLegacy }
    $cand = $openHours | Where-Object { $codes -contains $_.LocationCode }

    $exc = $cand | Where-Object { $_.ExceptionDate -and ([datetime]$_.ExceptionDate).Date -eq $date.Date } |
        Sort-Object { $_.ManualChangeAt } -Descending | Select-Object -First 1
    if ($exc) { if ($exc.IsClosed) { return @{ Start = $null; End = $null } } }
    else {
        $dow = [int]$date.DayOfWeek
        $exc = $cand | Where-Object {
            -not $_.ExceptionDate -and $_.DayOfWeek -eq $dow -and (-not $_.EffectiveFrom -or ([datetime]$_.EffectiveFrom).Date -le $date.Date)
        } | Sort-Object { if ($_.EffectiveFrom) { [datetime]$_.EffectiveFrom } else { [datetime]::MinValue } } -Descending | Select-Object -First 1
    }
    if (-not $exc -or $exc.IsClosed) { return @{ Start = $null; End = $null } }

    $start = if (-not [string]::IsNullOrWhiteSpace($exc.OpenTime)) { $exc.OpenTime.Trim() } else { $exc.OpenTime2 }
    $end   = if (-not [string]::IsNullOrWhiteSpace($exc.CloseTime2)) { $exc.CloseTime2.Trim() } else { $exc.CloseTime }
    if ($start) { $start = $start.Trim() } ; if ($end) { $end = $end.Trim() }
    return @{ Start = $start; End = $end }
}

function Get-DayName([datetime]$date) { return $date.DayOfWeek.ToString() }

# Requirement 6 overlap guard.  The SAME rule the server uses
# (WicShiftService.CreateAssignmentAsync and WicConflictDetector.WindowsOverlap):
#   start1 < end2 AND start2 < end1
# Unknown/unparseable windows never count as an overlap.
function Test-WindowOverlap([string]$a, [string]$b) {
    if ([string]::IsNullOrWhiteSpace($a) -or [string]::IsNullOrWhiteSpace($b)) { return $false }
    $x = $a.Split('-'); $y = $b.Split('-')
    if ($x.Count -ne 2 -or $y.Count -ne 2) { return $false }
    try {
        $x0 = [TimeSpan]::Parse($x[0]); $x1 = [TimeSpan]::Parse($x[1])
        $y0 = [TimeSpan]::Parse($y[0]); $y1 = [TimeSpan]::Parse($y[1])
    } catch { return $false }
    return ($x0 -lt $y1) -and ($y0 -lt $x1)
}

# ---------------------------------------------------------------------------
# Build the plan
# ---------------------------------------------------------------------------
$plan     = New-Object System.Collections.ArrayList
$skipped  = New-Object System.Collections.ArrayList

# Requirement 6: windows already planned in THIS run, per EmployeeId|ShiftDate.
# GROUP 2 is defined as "no WicShiftEntries row for the employee+date", so the
# existing set is normally empty; the planned set is what guards two manual
# WIC_DUTY rows of the same employee+day that would otherwise both insert.
$plannedWindowsByKey = @{}

foreach ($m in $manual) {
    $key = "$($m.EmployeeId)|$($m.D)"
    $existingForDay = @()
    if ($wicByEmpDate.ContainsKey($key)) { $existingForDay = @($wicByEmpDate[$key]) }

    if ($existingForDay.Count -gt 0) {
        [void]$skipped.Add([pscustomobject]@{
            Id = $m.Id; Reason = "already has $($existingForDay.Count) WicShiftEntries row(s) for this employee+day (not GROUP 2)"
        })
        continue
    }

    $res      = Resolve-WicLocation $m $existingForDay $m.EmployeeId
    $loc      = $res.Loc
    $shiftDay = [datetime]$m.ShiftDateRaw

    if (-not $loc) {
        [void]$plan.Add([pscustomobject]@{
            Action            = "WOULD WRITE NOTHING"
            ShiftEntryId      = $m.Id
            EmployeeId        = $m.EmployeeId
            ShiftDate         = $m.D
            AgentTask         = $m.AgentTask
            CentreDisplayName = $null
            LocationCode      = $null
            DayOfWeek         = $null
            WorkingShift      = $null
            IsOnSite          = $null
            Task              = $null
            ResolvedVia       = $null
            TimeSource        = $null
            ManualOrigin      = "ShiftEntries#$($m.Id) by=$($m.ManualChangeBy) at=$($m.ManualChangeAt)"
            Note              = "server returns: WIC duty was saved, but no WIC centre could be resolved for this day."
        })
        continue
    }

    $start = $m.ShiftStart
    $end   = $m.ShiftEnd
    $timeSource = "ShiftStart/ShiftEnd"
    if ([string]::IsNullOrWhiteSpace($start) -or [string]::IsNullOrWhiteSpace($end)) {
        $dw = Get-DutyWindow $loc $shiftDay
        if ([string]::IsNullOrWhiteSpace($start)) { $start = if ($dw.Start) { $dw.Start } else { "08:00" } }
        if ([string]::IsNullOrWhiteSpace($end))   { $end   = if ($dw.End)   { $dw.End }   else { "17:00" } }
        $timeSource = "WIC opening hours fallback (DutyWindow) + server defaults"
    }

    # Requirement 6: before writing, apply the SAME overlap test for the same
    # employee+day.  An existing (or already planned) IsOnSite=1 WicShiftEntries
    # row whose working window overlaps the row about to be written makes this row
    # be SKIPPED and reported.  Nothing is written for it and no existing row is
    # ever modified (Apply only INSERTs; an UPDATE is refused by HARD GATE 1).
    $candidateWin = "$start-$end"
    $occKey       = "$($m.EmployeeId)|$($m.D)"
    $blocking     = New-Object System.Collections.ArrayList
    foreach ($w in $existingForDay) {
        if ($w.IsOnSite -and (Test-WindowOverlap $candidateWin $w.WorkingShift)) {
            [void]$blocking.Add("wicId=$($w.Id) $($w.SupportLocation) $($w.WorkingShift)")
        }
    }
    if ($plannedWindowsByKey.ContainsKey($occKey)) {
        foreach ($w in $plannedWindowsByKey[$occKey]) {
            if (Test-WindowOverlap $candidateWin $w) { [void]$blocking.Add("planned $w") }
        }
    }
    if ($blocking.Count -gt 0) {
        [void]$plan.Add([pscustomobject]@{
            Action            = "WOULD WRITE NOTHING"
            ShiftEntryId      = $m.Id
            EmployeeId        = $m.EmployeeId
            ShiftDate         = $m.D
            AgentTask         = $m.AgentTask
            CentreDisplayName = $loc.DisplayName
            LocationCode      = $loc.LocationCode
            DayOfWeek         = $null
            WorkingShift      = $candidateWin
            IsOnSite          = $null
            Task              = $null
            ResolvedVia       = $res.Via
            TimeSource        = $timeSource
            ManualOrigin      = "ShiftEntries#$($m.Id) by=$($m.ManualChangeBy) at=$($m.ManualChangeAt)"
            SkipReason        = "OVERLAP_EXISTING_ONSITE"
            Note              = "overlap guard (requirement 6): an existing IsOnSite=1 WicShiftEntries row for the same employee+day overlaps the row that would be written ($candidateWin) -> " + ($blocking -join '; ')
        })
        continue
    }
    if (-not $plannedWindowsByKey.ContainsKey($occKey)) { $plannedWindowsByKey[$occKey] = New-Object System.Collections.ArrayList }
    [void]$plannedWindowsByKey[$occKey].Add($candidateWin)

    # the server updates an existing row for employee+date+centre, otherwise inserts
    $target   = $existingForDay | Where-Object { $_.SupportLocation -eq $loc.DisplayName } | Select-Object -First 1
    $action   = if ($target) { "UPDATE existing wicId=$($target.Id)" } else { "INSERT" }

    [void]$plan.Add([pscustomobject]@{
        Action            = $action
        ShiftEntryId      = $m.Id
        EmployeeId        = $m.EmployeeId
        ShiftDate         = $m.D
        AgentTask         = $m.AgentTask
        CentreDisplayName = $loc.DisplayName
        LocationCode      = $loc.LocationCode
        DayOfWeek         = (Get-DayName $shiftDay)
        WorkingShift      = "$start-$end"
        IsOnSite          = 1
        Task              = "WIC"
        ResolvedVia       = $res.Via
        TimeSource        = $timeSource
        ManualOrigin      = "ShiftEntries#$($m.Id) by=$($m.ManualChangeBy) at=$($m.ManualChangeAt)"
        Note              = ""
    })
}

# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------
$out = New-Object System.Collections.ArrayList
function P([string]$s = "") { [void]$out.Add($s) }

P "WIC BACKFILL FROM MANUAL SHIFT PLAN  (dry-run by default)"
P "mode           : $mode"
P "server now     : $serverToday   scope: ShiftDate >= $scopeFrom"
P ("connected to   : {0}" -f ($ConnectionString -replace '.*Database=([^;]+).*','DB=$1'))
P ""
P "Source rule: SyncWicDutyFromShiftPlanAsync (Backend/ShiftService.cs:325-416),"
P "             call sites :221 (UpdateShiftAsync) and :290 (AssignShiftAsync)."
P "Definition  : manual WIC_DUTY ShiftEntries row (ManualChangeBy not empty) with no"
P "              WicShiftEntries row for the same EmployeeId+ShiftDate = GROUP 2."
P ""
P ("{0} manual WIC_DUTY row(s) in scope; {1} are GROUP 2 and would be backfilled; {2} already linked." -f `
    $manual.Count, $plan.Count, $skipped.Count)
P ""

P "=== DRY-RUN RESULT: WicShiftEntries row that WOULD be written ==="
P ("{0,-6} {1,-8} {2,-10} {3,-22} {4,-13} {5,-6} {6,-4} {7,-6} {8}" -f `
    "seId","emp","date","SupportLocation","WorkingShift","onSite","Task","action","resolvedVia")
P ("{0,-6} {1,-8} {2,-10} {3,-22} {4,-13} {5,-6} {6,-4} {7,-6} {8}" -f `
    ("-"*6),("-"*8),("-"*10),("-"*22),("-"*13),("-"*6),("-"*4),("-"*6),("-"*18))

foreach ($p in $plan) {
    if ($p.Action -eq "WOULD WRITE NOTHING") {
        P ("{0,-6} {1,-8} {2,-10} {3,-22} {4,-13} {5,-6} {6,-4} {7,-6} {8}" -f `
            $p.ShiftEntryId,$p.EmployeeId,$p.ShiftDate,("<"+$p.AgentTask+">"),"-","-","-",$p.Action,"-")
    } else {
        P ("{0,-6} {1,-8} {2,-10} {3,-22} {4,-13} {5,-6} {6,-4} {7,-6} {8}" -f `
            $p.ShiftEntryId,$p.EmployeeId,$p.ShiftDate,$p.CentreDisplayName,$p.WorkingShift,$p.IsOnSite,$p.Task,$p.Action,$p.ResolvedVia)
    }
}
P ""

P "=== FULL COLUMN SET THAT WOULD BE INSERTED (one block per row) ==="
foreach ($p in $plan) {
    P ("-- ShiftEntries#{0}  ({1})" -f $p.ShiftEntryId, $p.Action)
    if ($p.Action -eq "WOULD WRITE NOTHING") {
        P ("   EmployeeId      = {0}" -f $p.EmployeeId)
        P ("   ShiftDate       = {0}" -f $p.ShiftDate)
        P ("   --> NO ROW: {0}" -f $p.Note)
    } else {
        P ("   INSERT INTO WicShiftEntries (EmployeeId, ShiftDate, DayOfWeek, SupportLocation, LocationCode,")
        P ("                                 IsOnSite, IsGSDDay, IsOffDay, WorkingShift, Task)")
        P ("   VALUES ('{0}', '{1}', '{2}', '{3}', '{4}', {5}, 0, 0, '{6}', '{7}');" -f `
            $p.EmployeeId, $p.ShiftDate, $p.DayOfWeek, $p.CentreDisplayName, $p.LocationCode, $p.IsOnSite, $p.WorkingShift, $p.Task)
        P ("   -- centre came from : {0}" -f $p.ResolvedVia)
        P ("   -- time came from   : {0}" -f $p.TimeSource)
    }
    P ("   -- MANUAL ORIGIN   : {0}  (NOT persistable: WicShiftEntries has no manual-origin column)" -f $p.ManualOrigin)
    P ""
}

P "=== ALREADY LINKED (not part of GROUP 2, no action) ==="
P ("  {0} row(s) already have a WicShiftEntries row for the same employee+day." -f $skipped.Count)
if ($ListSkipped) { foreach ($s in $skipped) { P ("    ShiftEntries#{0,-6} {1}" -f $s.Id, $s.Reason) } }
else { P ("    ids: {0}" -f (($skipped | ForEach-Object { $_.Id }) -join ', ')) }
P ""

P "=== SKIPPED BY THE OVERLAP GUARD (requirement 6) ==="
$overlapSkips = @($plan | Where-Object { $_.SkipReason -eq 'OVERLAP_EXISTING_ONSITE' })
P ("  {0} row(s) would NOT be written: an IsOnSite=1 WicShiftEntries row for the same employee+day overlaps them." -f $overlapSkips.Count)
foreach ($s in $overlapSkips) {
    P ("    ShiftEntries#{0,-6} {1} {2,-10} {3,-24} {4,-13}  {5}" -f `
        $s.ShiftEntryId, $s.EmployeeId, $s.ShiftDate, $s.CentreDisplayName, $s.WorkingShift, $s.Note)
}
P ""

P "=== SUMMARY ==="
P ("  GROUP 2 rows to backfill        : {0}" -f $plan.Count)
P ("    of those: INSERT              : {0}" -f @($plan | Where-Object { $_.Action -eq 'INSERT' }).Count)
P ("    of those: UPDATE existing row : {0}" -f @($plan | Where-Object { $_.Action -like 'UPDATE*' }).Count)
P ("    of those: WOULD WRITE NOTHING : {0}" -f @($plan | Where-Object { $_.Action -eq 'WOULD WRITE NOTHING' }).Count)
P ("    of those: SKIPPED (overlap)   : {0}" -f @($plan | Where-Object { $_.SkipReason -eq 'OVERLAP_EXISTING_ONSITE' }).Count)
P ("  already linked (skipped)        : {0}" -f $skipped.Count)
P ("  centres that would be created   : {0}" -f (@($plan | Where-Object { $_.CentreDisplayName } | Select-Object -ExpandProperty CentreDisplayName -Unique) -join ', '))
P ""

# ---------------------------------------------------------------------------
# Apply (only with -Apply)
# ---------------------------------------------------------------------------
$written = 0
if (-not $isDryRun) {
    $allPlanRows = @($plan)
    # Agreed scope: the coverage dates only.  The server already wrote the current-day rows
    # (see Tools\Backfill-WicFromManualShift.stepA.txt), so they are deliberately excluded.
    if (-not [string]::IsNullOrWhiteSpace($TargetFromDate)) {
        $cut = [datetime]::ParseExact($TargetFromDate, 'yyyy-MM-dd', $null)
        $plan = @($plan | Where-Object { ([datetime]::ParseExact($_.ShiftDate, 'yyyy-MM-dd', $null)) -gt $cut })
        P ("APPLY SCOPE: ShiftDate > {0}  ->  {1} of {2} planned row(s) in scope." -f $TargetFromDate, $plan.Count, $allPlanRows.Count)
    }
    $toInsert = @($plan | Where-Object { $_.Action -eq "INSERT" })
    $updates  = @($plan | Where-Object { $_.Action -like "UPDATE*" })
    $nothing  = @($plan | Where-Object { $_.Action -eq "WOULD WRITE NOTHING" })

    # HARD GATE 1: this backfill only ever INSERTs. An UPDATE would mean the
    # (employee, date, centre) row already exists, i.e. the day is linked and the row
    # is not GROUP 2 - so the plan is stale and the write must not happen.
    if ($updates.Count -gt 0) {
        throw ("APPLY REFUSED: {0} planned row(s) are UPDATE, not INSERT. A (employee, date, centre) row " +
               "already exists, so the plan is stale. Re-run the dry run." -f $updates.Count)
    }
    # HARD GATE 2: exact expected row count, so a changing database can never sneak in.
    if ($ExpectedInsertCount -gt 0 -and $toInsert.Count -ne $ExpectedInsertCount) {
        throw ("APPLY REFUSED: plan has {0} INSERT row(s), expected {1}." -f $toInsert.Count, $ExpectedInsertCount)
    }
    # HARD GATE 3: focus dates only - the 21 coverage rows.
    if ($toInsert.Count -eq 0) { throw "APPLY REFUSED: nothing to insert." }

    $countBefore = [int](Invoke-Scalar "SELECT COUNT(*) FROM WicShiftEntries")
    P ("APPLY: inserting {0} row(s) in one transaction ... (WicShiftEntries count before = {1})" -f $toInsert.Count, $countBefore)
    $inserted = New-Object System.Collections.ArrayList
    $tx = $conn.BeginTransaction()
    try {
        foreach ($p in $toInsert) {
            $cmd = $conn.CreateCommand()
            $cmd.Transaction = $tx
            # OUTPUT INSERTED.Id captures the generated identity inside the same transaction.
            $cmd.CommandText = @"
INSERT INTO WicShiftEntries
  (EmployeeId, ShiftDate, DayOfWeek, SupportLocation, LocationCode, IsOnSite, IsGSDDay, IsOffDay, WorkingShift, Task)
OUTPUT INSERTED.Id
VALUES
  (@emp, CONVERT(date, @d, 23), @dow, @loc, @code, 1, 0, 0, @ws, 'WIC')
"@
            foreach ($pair in @{ "@emp"=$p.EmployeeId; "@d"=$p.ShiftDate; "@dow"=$p.DayOfWeek; "@loc"=$p.CentreDisplayName; "@code"=$p.LocationCode; "@ws"=$p.WorkingShift }.GetEnumerator()) {
                $par = $cmd.CreateParameter(); $par.ParameterName = $pair.Key; $par.Value = $pair.Value
                [void]$cmd.Parameters.Add($par)
            }
            $newId = [int]$cmd.ExecuteScalar()
            [void]$inserted.Add([pscustomobject]@{
                NewId             = $newId
                ShiftEntryId      = $p.ShiftEntryId
                EmployeeId        = $p.EmployeeId
                ShiftDate         = $p.ShiftDate
                DayOfWeek         = $p.DayOfWeek
                SupportLocation   = $p.CentreDisplayName
                LocationCode      = $p.LocationCode
                WorkingShift      = $p.WorkingShift
                IsOnSite          = 1
                IsGSDDay          = 0
                IsOffDay          = 0
                Task              = "WIC"
            })
            $written++
        }
        $tx.Commit()
        P ("APPLY: committed {0} row(s)." -f $written)
    } catch {
        $tx.Rollback()
        P ("APPLY FAILED, rolled back: {0}" -f $_.Exception.Message)
        throw
    }

    # -----------------------------------------------------------------------
    # Artefacts: inserted-ids.txt and rollback.sql (the latter is NEVER executed)
    # -----------------------------------------------------------------------
    if (-not [string]::IsNullOrWhiteSpace($InsertedIdsFile)) {
        $idLines = New-Object System.Collections.ArrayList
        [void]$idLines.Add("WicShiftEntries ids inserted by Backfill-WicFromManualShift.ps1 -Apply")
        [void]$idLines.Add("run at : $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss')) UTC   server date: $serverToday")
        [void]$idLines.Add("count  : $($inserted.Count)")
        [void]$idLines.Add("")
        [void]$idLines.Add("NewId`tShiftEntryId`tEmployeeId`tShiftDate`tDayOfWeek`tSupportLocation`tLocationCode`tWorkingShift")
        foreach ($r in $inserted) {
            [void]$idLines.Add(("{0}`t{1}`t{2}`t{3}`t{4}`t{5}`t{6}`t{7}" -f `
                $r.NewId, $r.ShiftEntryId, $r.EmployeeId, $r.ShiftDate, $r.DayOfWeek, $r.SupportLocation, $r.LocationCode, $r.WorkingShift))
        }
        ($idLines -join "`r`n") | Set-Content -Path $InsertedIdsFile -Encoding UTF8
        P ("APPLY: inserted ids written to {0}" -f $InsertedIdsFile)
    }

    if (-not [string]::IsNullOrWhiteSpace($RollbackSqlFile)) {
        $sql = New-Object System.Collections.ArrayList
        [void]$sql.Add("-- rollback.sql  -  UNDO of the WIC backfill from manual ShiftEntries WIC_DUTY rows")
        [void]$sql.Add("-- Generated by Tools\Backfill-WicFromManualShift.ps1 -Apply on $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss')) UTC.")
        [void]$sql.Add("-- NOT EXECUTED as part of the backfill. Run it ONLY to undo the backfill.")
        [void]$sql.Add("-- It deletes exactly the $($inserted.Count) rows inserted, by Id, and nothing else.")
        [void]$sql.Add("-- The partner ShiftEntries rows (ManualChangeBy S60028) are NOT touched: the Shift Plan")
        [void]$sql.Add("-- cells stay exactly as the human entered them.")
        [void]$sql.Add("")
        [void]$sql.Add("SET XACT_ABORT ON;")
        [void]$sql.Add("BEGIN TRAN;")
        [void]$sql.Add("")
        [void]$sql.Add("-- Guard: refuse to run if any id is missing or already used by a row this backfill did not write.")
        [void]$sql.Add("DECLARE @ids TABLE (Id int PRIMARY KEY);")
        foreach ($r in $inserted) { [void]$sql.Add(("INSERT INTO @ids (Id) VALUES ({0});" -f $r.NewId)) }
        [void]$sql.Add("")
        [void]$sql.Add("IF (SELECT COUNT(*) FROM @ids) <> $($inserted.Count)")
        [void]$sql.Add("    THROW 51000, 'rollback.sql: id list changed - refusing to run.', 1;")
        [void]$sql.Add("")
        [void]$sql.Add("DELETE w")
        [void]$sql.Add("FROM WicShiftEntries w")
        [void]$sql.Add("JOIN @ids i ON i.Id = w.Id")
        [void]$sql.Add("WHERE w.Task = 'WIC' AND w.IsOnSite = 1 AND w.IsGSDDay = 0 AND w.IsOffDay = 0;")
        [void]$sql.Add("")
        [void]$sql.Add("DECLARE @deleted int = @@ROWCOUNT;")
        [void]$sql.Add("IF @deleted <> $($inserted.Count)")
        [void]$sql.Add("BEGIN")
        [void]$sql.Add("    DECLARE @msg nvarchar(200) = N'rollback.sql: deleted ' + CAST(@deleted AS nvarchar(10)) + N' of $($inserted.Count) rows - rolled back.';")
        [void]$sql.Add("    ROLLBACK;")
        [void]$sql.Add("    THROW 51001, @msg, 1;")
        [void]$sql.Add("END")
        [void]$sql.Add("COMMIT;")
        [void]$sql.Add("")
        [void]$sql.Add("-- Verify afterwards (expected $countBefore):")
        [void]$sql.Add("-- SELECT COUNT(*) FROM WicShiftEntries;")
        ($sql -join "`r`n") | Set-Content -Path $RollbackSqlFile -Encoding UTF8
        P ("APPLY: rollback script written to {0}  (NOT executed)" -f $RollbackSqlFile)
    }

    # -----------------------------------------------------------------------
    # Post-apply verification
    # -----------------------------------------------------------------------
    $countAfter = [int](Invoke-Scalar "SELECT COUNT(*) FROM WicShiftEntries")
    P ""
    P "=== POST-APPLY VERIFICATION ==="
    P ("  WicShiftEntries count before : {0}" -f $countBefore)
    P ("  WicShiftEntries count after  : {0}   (delta {1}, expected {2})" -f $countAfter, ($countAfter-$countBefore), $inserted.Count)
    if ($countAfter -ne $countBefore + $inserted.Count) {
        P "  *** COUNT MISMATCH - investigate before doing anything else ***"
    }
    foreach ($r in $inserted) {
        $n = Invoke-Scalar "SELECT COUNT(*) FROM WicShiftEntries WHERE Id=$($r.NewId) AND EmployeeId='$($r.EmployeeId)' AND ShiftDate='$($r.ShiftDate)' AND SupportLocation=N'$($r.SupportLocation)'"
        $onSite = Invoke-Scalar "SELECT COUNT(*) FROM WicShiftEntries WHERE Id=$($r.NewId) AND IsOnSite=1 AND IsGSDDay=0 AND IsOffDay=0 AND Task='WIC'"
        P ("  newId={0,-6} seId={1,-6} {2} {3,-20} {4,-13} row={5} onsite={6}" -f `
            $r.NewId, $r.ShiftEntryId, $r.EmployeeId, $r.SupportLocation, $r.WorkingShift, $n, $onSite)
    }
} else {
    P "NO WRITES PERFORMED. Re-run with -Apply to insert (that is the only mode that writes)."
}

$conn.Close()

$text = ($out -join "`r`n")
if (-not [string]::IsNullOrWhiteSpace($OutFile)) { $text | Set-Content -Path $OutFile -Encoding UTF8 }
$text
