# Backfill-WicFromManualShift.verify.ps1
#
# STEP A of the pre-write gate: prove that the row the backfill script WOULD write for the
# 21 targeted manual ShiftEntries rows is populated by exactly the same logic as the row
# SyncWicDutyFromShiftPlanAsync already wrote for the reference rows (WicShiftEntries
# Id=67764 plus at least two more of the 13 created after 13:35 UTC on 2026-10-12).
#
# READ ONLY.  Prints a column-by-column comparison table and an explicit verdict.
#
# Usage:
#   pwsh -File Tools\Backfill-WicFromManualShift.verify.ps1

[CmdletBinding()]
param(
    [string] $ConnectionString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;",
    [string] $DryRunFile = "",
    [int[]]  $SyncSampleId = @(67764, 67760, 67763),
    [string] $OutFile = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Data
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DryRunFile)) {
    $DryRunFile = Join-Path $PSScriptRoot "Backfill-WicFromManualShift.dryrun.txt"
}

# ---------------------------------------------------------------------------
# 1. Target plan (the 21 rows) - read straight out of the live dry-run report
# ---------------------------------------------------------------------------
$dry = Get-Content -LiteralPath $DryRunFile -Encoding UTF8
$plan = New-Object System.Collections.ArrayList
$inTable = $false
foreach ($l in $dry) {
    if ($l -like "=== DRY-RUN RESULT*") { $inTable = $true; continue }
    if ($inTable) {
        if ($l -like "===*") { break }
        if ($l -match '^(\d{4,6})\s+(\d{7})\s+(\d{4}-\d{2}-\d{2})\s+(.+?)\s{2,}(\d{2}:\d{2}-\d{2}:\d{2}|-)\s+(\d|-)\s+(WIC|-)\s+(INSERT|UPDATE.*|WOULD WRITE NOTHING)\s+(.*)$') {
            [void]$plan.Add([pscustomobject]@{
                SeId = [int]$Matches[1]; EmpId = $Matches[2]; Date = $Matches[3];
                Centre = $Matches[4].Trim(); Shift = $Matches[5].Trim(); OnSite = $Matches[6];
                Task = $Matches[7]; Action = $Matches[8].Trim(); Via = $Matches[9].Trim()
            })
        }
    }
}

# The 21 target rows = the plan rows for the coverage dates (all but the top 5 of Oct 12).
$target = @($plan | Where-Object { $_.Date -ne "2026-10-12" })
if ($plan.Count -ne 26) { Write-Warning "dry-run plan rows parsed: $($plan.Count) (expected 26)" }
if ($target.Count -ne 21) { Write-Warning "target rows (coverage dates): $($target.Count) (expected 21)" }

# ---------------------------------------------------------------------------
# 2. DB plumbing
# ---------------------------------------------------------------------------
$conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
$conn.Open()

function Invoke-Rows([string]$sql, [hashtable]$params = @{}) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
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
            $v = $rdr.GetValue($i); if ($v -is [System.DBNull]) { $v = $null }
            $h[$rdr.GetName($i)] = $v
        }
        [void]$out.Add([pscustomobject]$h)
    }
    $rdr.Close(); return ,$out
}

$locations  = Invoke-Rows "SELECT LocationCode, DisplayName, LocationCodeLegacy, IsActive FROM WicLocations"
$openHours  = Invoke-Rows "SELECT LocationCode, DayOfWeek, OpenTime, CloseTime, OpenTime2, CloseTime2, IsClosed, ExceptionDate, EffectiveFrom, ManualChangeAt FROM WicOpeningHours"
$mainAssign = Invoke-Rows "SELECT LocationCode, EmployeeName, AssignmentType, IsActive FROM WicAgentAssignments WHERE IsActive = 1 AND AssignmentType = 'MAIN'"
$employees  = Invoke-Rows "SELECT EmployeeId, FullName, IsActive FROM Employees"

$NonLocationValues = @("WIC","VWIC","Voice","Backlog","Dispatcher","SME","SSP","GSD")
function Test-NonLocationValue([string]$task) {
    if ([string]::IsNullOrWhiteSpace($task)) { return $false }
    return ($NonLocationValues -contains $task.Trim())
}

# Resolve-WicLocation: byte-for-byte copy of ShiftService.cs:342-368 (cases 1-4).
function Resolve-WicLocation($row, $existingRowsForDay, $empId) {
    if (-not [string]::IsNullOrWhiteSpace($row.LocationId)) {
        $loc = $locations | Where-Object { $_.IsActive -and $_.LocationCode -eq $row.LocationId } | Select-Object -First 1
        if ($loc) { return @{ Loc = $loc; Via = "1 LocationId hint" } }
    }
    if (-not [string]::IsNullOrWhiteSpace($row.AgentTask) -and -not (Test-NonLocationValue $row.AgentTask)) {
        $loc = $locations | Where-Object { $_.IsActive -and ($_.DisplayName -eq $row.AgentTask -or $_.LocationCode -eq $row.AgentTask) } | Select-Object -First 1
        if ($loc) { return @{ Loc = $loc; Via = "2 AgentTask exact" } }
    }
    $onSite = $existingRowsForDay | Where-Object { $_.IsOnSite -and -not [string]::IsNullOrWhiteSpace($_.SupportLocation) } | Select-Object -First 1
    if ($onSite) {
        $loc = $locations | Where-Object { $_.IsActive -and $_.DisplayName -eq $onSite.SupportLocation } | Select-Object -First 1
        if ($loc) { return @{ Loc = $loc; Via = "3 existing on-site WIC row" } }
    }
    $emp = $employees | Where-Object { $_.EmployeeId -eq $empId } | Select-Object -First 1
    if ($emp -and $emp.FullName) {
        $main = $mainAssign | Where-Object { $_.EmployeeName -eq $emp.FullName } | Select-Object -First 1
        if ($main) {
            $loc = $locations | Where-Object { $_.IsActive -and ($_.LocationCode -eq $main.LocationCode -or $_.LocationCodeLegacy -eq $main.LocationCode) } | Select-Object -First 1
            if ($loc) { return @{ Loc = $loc; Via = "4 employee MAIN assignment" } }
        }
    }
    return @{ Loc = $null; Via = $null }
}

# Get-DutyWindow: copy of WicHoursResolver.Resolve + DutyWindow (WicHoursResolver.cs:50-98).
function Get-DutyWindow($loc, [datetime]$date) {
    if (-not $loc) { return @{ Start = $null; End = $null } }
    $codes = @($loc.LocationCode); if ($loc.LocationCodeLegacy) { $codes += $loc.LocationCodeLegacy }
    $cand = $openHours | Where-Object { $codes -contains $_.LocationCode }
    $exc = $cand | Where-Object { $_.ExceptionDate -and ([datetime]$_.ExceptionDate).Date -eq $date.Date } |
        Sort-Object { $_.ManualChangeAt } -Descending | Select-Object -First 1
    if (-not $exc) {
        $dow = [int]$date.DayOfWeek
        $exc = $cand | Where-Object {
            -not $_.ExceptionDate -and $_.DayOfWeek -eq $dow -and (-not $_.EffectiveFrom -or ([datetime]$_.EffectiveFrom).Date -le $date.Date)
        } | Sort-Object { if ($_.EffectiveFrom) { [datetime]$_.EffectiveFrom } else { [datetime]::MinValue } } -Descending | Select-Object -First 1
    }
    if (-not $exc -or $exc.IsClosed) { return @{ Start = $null; End = $null; Row = $exc } }
    $start = if (-not [string]::IsNullOrWhiteSpace($exc.OpenTime)) { $exc.OpenTime.Trim() } else { $exc.OpenTime2 }
    $end   = if (-not [string]::IsNullOrWhiteSpace($exc.CloseTime2)) { $exc.CloseTime2.Trim() } else { $exc.CloseTime }
    if ($start) { $start = $start.Trim() }; if ($end) { $end = $end.Trim() }
    return @{ Start = $start; End = $end; Row = $exc }
}

# ---------------------------------------------------------------------------
# 3. Load source ShiftEntries + the WicShiftEntries the sync already wrote
# ---------------------------------------------------------------------------
# plan rows + the source ShiftEntries behind the sync sample rows (not in the plan)
$sampleSe = Invoke-Rows ("SELECT se.Id FROM WicShiftEntries w JOIN ShiftEntries se " +
    "ON se.EmployeeId = w.EmployeeId AND se.ShiftDate = w.ShiftDate " +
    "WHERE w.Id IN (" + ($SyncSampleId -join ",") + ")")
$allSeIds = @(@($plan | ForEach-Object { $_.SeId }) + @($sampleSe | ForEach-Object { $_.Id }) | Select-Object -Unique)
$seList = Invoke-Rows ("SELECT Id, EmployeeId, CONVERT(varchar(10),ShiftDate,23) D, ShiftType, ShiftStart, ShiftEnd, " +
    "AgentTask, LocationId, IsWicDuty, LTRIM(RTRIM(ManualChangeBy)) ManualChangeBy, " +
    "CONVERT(varchar(19),ManualChangeAt,120) ManualChangeAt, ManualChangeNote, SourceModule, AutoGenerated " +
    "FROM ShiftEntries WHERE Id IN (" + ($allSeIds -join ",") + ") ORDER BY Id")
$seById = @{}; foreach ($s in $seList) { $seById[[int]$s.Id] = $s }

$syncRows = Invoke-Rows ("SELECT w.Id, w.EmployeeId, CONVERT(varchar(10),w.ShiftDate,23) D, w.DayOfWeek, w.SupportLocation, " +
    "w.WicOpeningHours, w.WorkingShift, w.IsOnSite, w.IsGSDDay, w.IsOffDay, w.Task, w.LocationCode " +
    "FROM WicShiftEntries w WHERE w.Id IN (" + ($SyncSampleId -join ",") + ") ORDER BY w.Id")
if ($syncRows.Count -ne $SyncSampleId.Count) { throw "one or more sample WicShiftEntries ids not found: $($SyncSampleId -join ',')" }

$wicForSamples = Invoke-Rows ("SELECT w.Id, w.EmployeeId, CONVERT(varchar(10),w.ShiftDate,23) D, w.IsOnSite, w.SupportLocation " +
    "FROM WicShiftEntries w WHERE w.Id IN (" + ($SyncSampleId -join ",") + ")")

# ---------------------------------------------------------------------------
# 4. Build the comparison
# ---------------------------------------------------------------------------
$out = New-Object System.Collections.ArrayList
function P([string]$s = "") { [void]$out.Add($s) }

P "STEP A - SCRIPT vs SERVER SYNC (SyncWicDutyFromShiftPlanAsync, ShiftService.cs:325-416)"
P "read only   : yes (no writes in this script)"
P "dry-run file: $DryRunFile"
P "target rows : $($target.Count) manual WIC_DUTY ShiftEntries rows on the coverage dates"
P "sync samples: WicShiftEntries Id $(($SyncSampleId) -join ', ')"
P ""

$fields = @("ShiftDate","DayOfWeek","SupportLocation","LocationCode","WicOpeningHours","WorkingShift","IsOnSite","IsGSDDay","IsOffDay","Task")
$compare = New-Object System.Collections.ArrayList

foreach ($w in $syncRows) {
    $se = $seById.Values | Where-Object { $_.EmployeeId -eq $w.EmployeeId -and $_.D -eq $w.D } | Select-Object -First 1
    if (-not $se) { throw "no source ShiftEntries for sync sample wicId=$($w.Id)" }

    $existingForDay = @($wicForSamples | Where-Object { $_.EmployeeId -eq $w.EmployeeId -and $_.D -eq $w.D -and $_.Id -ne $w.Id })
    $res = Resolve-WicLocation $se $existingForDay $se.EmployeeId
    $loc = $res.Loc
    $shiftDay = [datetime]::ParseExact($se.D, 'yyyy-MM-dd', $null)

    # Sync wrote this row, so at THIS point in the server call the day had no other row:
    # the script must therefore reproduce the row from location + ShiftStart/ShiftEnd.
    $start = $se.ShiftStart; $end = $se.ShiftEnd
    $timeSource = "ShiftStart/ShiftEnd (as the server does when the cell carries times)"
    if ([string]::IsNullOrWhiteSpace($start) -or [string]::IsNullOrWhiteSpace($end)) {
        $dw = Get-DutyWindow $loc $shiftDay
        if ([string]::IsNullOrWhiteSpace($start)) { $start = if ($dw.Start) { $dw.Start } else { "08:00" } }
        if ([string]::IsNullOrWhiteSpace($end))   { $end   = if ($dw.End)   { $dw.End }   else { "17:00" } }
        $timeSource = "WicHoursResolver.DutyWindow + server defaults"
    }

    $expected = [ordered]@{
        ShiftDate       = $se.D
        DayOfWeek       = $shiftDay.DayOfWeek.ToString()
        SupportLocation = if ($loc) { $loc.DisplayName } else { $null }
        LocationCode    = if ($loc) { $loc.LocationCode } else { $null }
        WicOpeningHours = $null
        WorkingShift    = "$start-$end"
        IsOnSite        = $true
        IsGSDDay        = $false
        IsOffDay        = $false
        Task            = "WIC"
    }
    $actual = [ordered]@{
        ShiftDate       = $w.D
        DayOfWeek       = $w.DayOfWeek
        SupportLocation = $w.SupportLocation
        LocationCode    = $w.LocationCode
        WicOpeningHours = $w.WicOpeningHours
        WorkingShift    = $w.WorkingShift
        IsOnSite        = $w.IsOnSite
        IsGSDDay        = $w.IsGSDDay
        IsOffDay        = $w.IsOffDay
        Task            = $w.Task
    }
    [void]$compare.Add([pscustomobject]@{ WicId = $w.Id; SeId = $se.Id; Emp = $w.EmployeeId; D = $w.D
        Centre = $w.SupportLocation; Via = $res.Via; TimeSource = $timeSource
        Expected = $expected; Actual = $actual })
}

P "=== COLUMN-BY-COLUMN: server-synced row  vs  script rule ==="
P ""
foreach ($c in $compare) {
    P ("WicShiftEntries Id={0}  (source ShiftEntries Id={1}, emp={2}, {3}, centre={4})" -f $c.WicId,$c.SeId,$c.Emp,$c.D,$c.Centre)
    P ("  location case resolved by script : {0}" -f $c.Via)
    P ("  time source per script           : {0}" -f $c.TimeSource)
    P ("  {0,-16} {1,-46} {2,-46} {3}" -f "column","server-synced row","script rule","match")
    P ("  {0,-16} {1,-46} {2,-46} {3}" -f ("-"*16),("-"*46),("-"*46),("-"*5))
    $allMatch = $true
    foreach ($f in $fields) {
        $a = $c.Actual[$f]; $e = $c.Expected[$f]
        $m = if ([string]::IsNullOrEmpty([string]$a) -and [string]::IsNullOrEmpty([string]$e)) { $true } else { "$a" -eq "$e" }
        if (-not $m) { $allMatch = $false }
        P ("  {0,-16} {1,-46} {2,-46} {3}" -f $f, "[$a]", "[$e]", $(if ($m) { "OK" } else { "*** DIFF ***" }))
    }
    P ("  VERDICT for Id={0}: {1}" -f $c.WicId, $(if ($allMatch) { "MATCH on every WicShiftEntries column" } else { "DIVERGES - script must be reconciled" }))
    P ""
}

# ---------------------------------------------------------------------------
# 5. plan-21.csv integrity + full 21-row expected insert list
# ---------------------------------------------------------------------------
P "=== plan-21.csv CHECK ==="
P ""
$planCsv = "C:\ProgramData\PulseRollback\wic-backfill-20261012-162439\plan-21.csv"
if (Test-Path -LiteralPath $planCsv) {
    $csv = Import-Csv -LiteralPath $planCsv
    P ("plan-21.csv rows: {0}, header: {1}" -f $csv.Count, (($csv[0].PSObject.Properties.Name) -join ','))
    $byTuple = @{}
    foreach ($r in $csv) { $byTuple["$($r.SeId)|$($r.Date)"] = $r }
    $mismatch = 0
    foreach ($t in $target) {
        $k = "$($t.SeId)|$($t.Date)"
        if (-not $byTuple.ContainsKey($k)) { P ("  MISSING in plan-21.csv: {0}" -f $k); $mismatch++; continue }
        $r = $byTuple[$k]
        if ($r.EmpId -ne $t.EmpId -or $r.Centre -ne $t.Centre -or $r.Shift -ne $t.Shift) {
            P ("  MISALIGNED row {0}: csv=[Emp={1} Centre={2} Shift={3}] dry-run=[Emp={4} Centre={5} Shift={6}]" -f `
                $k, $r.EmpId, $r.Centre, $r.Shift, $t.EmpId, $t.Centre, $t.Shift)
            $mismatch++
        }
    }
    if ($mismatch -eq 0) { P "  plan-21.csv is a faithful extraction of the 21 planned rows (EmpId/Date/Centre/Shift all equal)." }
    else { P ("  plan-21.csv has {0} row(s) whose data columns are shifted/misaligned - the dry-run is the authority." -f $mismatch) }
} else { P "  plan-21.csv NOT FOUND at $planCsv" }
P ""

P "=== THE 21 ROWS THE SCRIPT WOULD INSERT (authoritative, from the live dry-run) ==="
P ("  {0,-6} {1,-8} {2,-10} {3,-24} {4,-13} {5,-12}" -f "seId","emp","date","SupportLocation","WorkingShift","locationResolved")
P ("  {0,-6} {1,-8} {2,-10} {3,-24} {4,-13} {5,-12}" -f ("-"*6),("-"*8),("-"*10),("-"*24),("-"*13),("-"*12))
foreach ($t in $target) {
    $se = $seById[[int]$t.SeId]
    P ("  {0,-6} {1,-8} {2,-10} {3,-24} {4,-13} {5,-12}" -f $t.SeId,$t.EmpId,$t.Date,$t.Centre,$t.Shift,$t.Via)
}
P ""
P ("groups: " + (($target | Group-Object Date | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join "  "))

$conn.Close()

$text = ($out -join "`r`n")
if (-not [string]::IsNullOrWhiteSpace($OutFile)) { $text | Set-Content -Path $OutFile -Encoding UTF8 }
$text
