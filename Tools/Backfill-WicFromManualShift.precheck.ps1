# Backfill-WicFromManualShift.precheck.ps1
#
# STEP B/C + PRE-WRITE GATE, immediately before the write. READ ONLY.
#
#   B. Lists every writer in Backend\*.cs that mutates an EXISTING WicShiftEntries row
#      (UPDATE or DELETE) and says whether it also proves / touches the paired ShiftEntries
#      row for the same employee+date.  Any "NO" would stop the backfill.
#   C. Re-verifies, at this instant:
#        - the plan is exactly the 21 coverage rows,
#        - not one of the 21 (employee, date) pairs has ANY WicShiftEntries row,
#        - the rollback backup C:\ProgramData\PulseRollback\wic-backfill-20261012-162439\
#          WicShiftEntries.csv still exists with 23981 data rows,
#        - the live WicShiftEntries count and max Id,
#        - the WorkingShift the script would write equals ShiftStart-ShiftEnd.
#
# Usage:  pwsh -File Tools\Backfill-WicFromManualShift.precheck.ps1

[CmdletBinding()]
param(
    [string] $ConnectionString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;",
    [string] $DryRunFile = "",
    [string] $BackupCsv  = "C:\ProgramData\PulseRollback\wic-backfill-20261012-162439\WicShiftEntries.csv",
    [string] $OutFile    = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Data
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DryRunFile)) {
    $DryRunFile = Join-Path $PSScriptRoot "Backfill-WicFromManualShift.dryrun.txt"
}

$out = New-Object System.Collections.ArrayList
function P([string]$s = "") { [void]$out.Add($s) }
$fail = New-Object System.Collections.ArrayList

# ---------------------------------------------------------------------------
# Plan (21 coverage rows) from the live dry run
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
                Centre = $Matches[4].Trim(); Shift = $Matches[5].Trim(); Action = $Matches[8].Trim()
            })
        }
    }
}
$target = @($plan | Where-Object { $_.Date -ne "2026-10-12" })

$conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
$conn.Open()
function Invoke-Rows([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
    $rdr = $cmd.ExecuteReader(); $o = New-Object System.Collections.ArrayList
    while ($rdr.Read()) { $h = @{}; for ($i=0; $i -lt $rdr.FieldCount; $i++) { $v = $rdr.GetValue($i); if ($v -is [System.DBNull]) { $v = $null }; $h[$rdr.GetName($i)] = $v }; [void]$o.Add([pscustomobject]$h) }
    $rdr.Close(); return ,$o
}
function Invoke-Scalar([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300; return $cmd.ExecuteScalar()
}

P "PRE-WRITE GATE (step C) + WRITER AUDIT (step B)"
P "read only : yes"
P "server now: $(Invoke-Scalar 'SELECT CONVERT(varchar(19), GETDATE(), 120)') (SQL local = UTC on this instance)"
P ""

# --- B: static audit -------------------------------------------------------
P "=== B. WRITERS THAT MUTATE AN EXISTING WicShiftEntries ROW ==="
P ""
$writers = @(
  [pscustomobject]@{ File="Backend\Modules\SubstitutionModule.cs"; Line=145; Op="RemoveRange(stale)";         Paired="YES - updates/creates the same (emp,date) ShiftEntries row a few lines above (line 109-136); PreserveManualShift guard at line 72" },
  [pscustomobject]@{ File="Backend\Modules\SubstitutionModule.cs"; Line=149; Op="UPDATE target row";          Paired="YES - same (emp,date) ShiftEntries row in the same transaction" },
  [pscustomobject]@{ File="Backend\RosterService.cs";               Line=667; Op="RemoveRange(WIC of replaced WIC_DUTY day)"; Paired="YES - only for a ShiftEntries row being replaced in the same loop; PreserveManualShift at line 651 skips manual rows first" },
  [pscustomobject]@{ File="Backend\RosterService.cs";               Line=1173; Op="RemoveRange(WIC of batch)"; Paired="YES - dates come from the WIC_DUTY ShiftEntries rows of the batch being deleted (line 1162-1164)" },
  [pscustomobject]@{ File="Backend\RosterService.cs";               Line=582; Op="UPDATE (upsert by emp+date+centre)"; Paired="YES - the ShiftEntries row was just created/replaced in the same unit of work" },
  [pscustomobject]@{ File="Backend\ShiftService.cs";                Line=338; Op="IsOnSite=false (cell left WIC_DUTY)"; Paired="YES - that ShiftEntries row IS the caller (UpdateShiftAsync/AssignShiftAsync)" },
  [pscustomobject]@{ File="Backend\ShiftService.cs";                Line=390; Op="UPDATE (upsert by emp+date+centre)"; Paired="YES - the same ShiftEntries cell is the source of the row" },
  [pscustomobject]@{ File="Backend\ShiftSyncService.cs";            Line=210; Op="RemoveRange(keySet)";        Paired="YES - keySet is exactly the set of ShiftEntries rows mutated in the same method (line 188-198); PreserveManualShift at line 185 skips manual rows" },
  [pscustomobject]@{ File="Backend\WicScheduleService.cs";          Line=317; Op="RemoveRange(toDelete)";     Paired="YES - deletes the matching WIC_DUTY ShiftEntries row (line 333-352) and removes manual days first (line 301-308, PreserveManualShift)" },
  [pscustomobject]@{ File="Backend\WicShiftService.cs";             Line=142; Op="IsOnSite=false / SupportLocation=null"; Paired="PARTIAL - PATCH /api/wic/{id} is the interactive WIC Attendance cell edit by row Id; it does NOT touch the ShiftEntries row" },
  [pscustomobject]@{ File="Backend\WicShiftService.cs";             Line=587; Op="UPDATE (upsert by emp+date+centre)"; Paired="YES - /api/wic/assignments + /api/wic/shifts build/update the ShiftEntries row in the same method (line 499-570 / 715-765)" },
  [pscustomobject]@{ File="Backend\WicShiftService.cs";             Line=884; Op="Remove(superseded)";        Paired="YES - the same method updates the paired ShiftEntries row (line 925-931) and it is the human Assign-Agent action" },
  [pscustomobject]@{ File="Backend\WicShiftService.cs";             Line=1046; Op="IsOnSite=false, IsGSDDay=true"; Paired="YES - the paired ShiftEntries row is created/updated in the same loop (line 1012-1033)" },
  [pscustomobject]@{ File="Backend\RolloutService.cs";              Line=526; Op="Remove(row)";              Paired="YES - by the Id the rollout feature itself recorded in WicRolloutWorkers.WicShiftEntryId, and it drops its own ShiftEntries row in the same call (line 530-538)" },
  [pscustomobject]@{ File="Backend\Program.cs";                     Line=728; Op="DELETE SupportLocation IS NULL rows"; Paired="NO - POST /api/admin/cleanup-wic-orphans; it is matched by a non-NULL SIBLING WicShiftEntries row, never by ShiftEntries. Manual one-shot, not called at startup (line 657-658). It can only ever delete rows with SupportLocation IS NULL." }
)
P ("  {0,-46} {1,-6} {2,-42} {3}" -f "file", "line", "operation", "paired ShiftEntries proof")
P ("  {0,-46} {1,-6} {2,-42} {3}" -f ("-"*46),("-"*6),("-"*42),("-"*40))
foreach ($w in $writers) { P ("  {0,-46} {1,-6} {2,-42} {3}" -f $w.File, $w.Line, $w.Op, $w.Paired) }
P ""
P "  Reading of the two non-YES entries:"
P "   * WicShiftService.cs:142 (PATCH /api/wic/{id}) changes IsOnSite/Task/Location on ONE WicShiftEntries"
P "     row chosen by its own Id - it is the human WIC Attendance cell edit and it deliberately does not"
P "     rewrite the Shift Plan cell. It cannot delete a row and it can only flip a row the human picked,"
P "     so it is not a rogue writer; it is a second human surface."
P "   * Program.cs:728 (cleanup-wic-orphans) deletes rows whose SupportLocation IS NULL and which have a"
P "     non-NULL sibling for the same (employee, date). It never runs at startup (Program.cs:657-658)."
P "     The 21 backfilled rows all have a non-NULL SupportLocation (Trier, Muelheim, Landshut, Stadland,"
P "     Emmerthal, Berlin - Gaussstr., Arnsberg, Recklinghausen, Brokdorf), so this endpoint can never"
P "     delete one of them. The query below proves no such NULL sibling exists for the 21 pairs."
P ""

$nullSiblings = Invoke-Rows @"
SELECT w.EmployeeId, CONVERT(varchar(10), w.ShiftDate, 23) D, COUNT(*) Cnt
FROM WicShiftEntries w
WHERE w.SupportLocation IS NULL
GROUP BY w.EmployeeId, CONVERT(varchar(10), w.ShiftDate, 23)
"@
$nullKeys = @{}; foreach ($n in $nullSiblings) { $nullKeys["$($n.EmployeeId)|$($n.D)"] = $n.Cnt }
$hit = @($target | Where-Object { $nullKeys.ContainsKey("$($_.EmpId)|$($_.Date)") })
P ("  NULL-SupportLocation rows on any of the 21 (employee, date) pairs: {0}" -f $hit.Count)
if ($hit.Count -gt 0) { [void]$fail.Add("cleanup-wic-orphans could delete a backfilled row: NULL sibling exists") }
P ""

# --- C: pre-write re-verification -----------------------------------------
P "=== C. RE-VERIFICATION IMMEDIATELY BEFORE THE WRITE ==="
P ""

# C1: plan size and shape
P ("  C1 rows in plan (coverage dates)      : {0}   (must be 21)" -f $target.Count)
if ($target.Count -ne 21) { [void]$fail.Add("plan row count is $($target.Count), expected 21") }
$badAction = @($target | Where-Object { $_.Action -ne "INSERT" })
P ("  C1 rows whose action is not INSERT    : {0}   (must be 0)" -f $badAction.Count)
if ($badAction.Count -gt 0) { [void]$fail.Add("plan contains non-INSERT actions") }
$dates = ($target | Group-Object Date | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join "  "
P ("  C1 date groups                        : {0}" -f $dates)

# C2: no WicShiftEntries row for any of the 21 (employee, date) pairs
$pairs = ($target | ForEach-Object { "('$($_.EmpId)','$($_.Date)')" }) -join ","
$collisions = Invoke-Rows @"
SELECT w.EmployeeId, CONVERT(varchar(10), w.ShiftDate, 23) D, COUNT(*) Cnt
FROM WicShiftEntries w
JOIN (VALUES $pairs) AS p(EmpId, D) ON p.EmpId = w.EmployeeId AND CONVERT(varchar(10), w.ShiftDate, 23) = p.D
GROUP BY w.EmployeeId, CONVERT(varchar(10), w.ShiftDate, 23)
"@
P ("  C2 (employee, date) pairs with any WicShiftEntries row : {0}   (must be 0)" -f $collisions.Count)
foreach ($c in $collisions) { P ("      COLLISION: {0} {1} -> {2} row(s)" -f $c.EmployeeId, $c.D, $c.Cnt) }
if ($collisions.Count -gt 0) { [void]$fail.Add("a target (employee,date) already has a WicShiftEntries row") }

# C2b: the partner ShiftEntries rows must still be the manual WIC_DUTY rows we planned
$seIds = ($target | ForEach-Object { $_.SeId }) -join ","
$src = Invoke-Rows @"
SELECT se.Id, se.EmployeeId, CONVERT(varchar(10),se.ShiftDate,23) D, se.ShiftType, se.IsWicDuty,
       se.ShiftStart, se.ShiftEnd, LTRIM(RTRIM(ISNULL(se.ManualChangeBy,''))) ManualChangeBy, se.AgentTask
FROM ShiftEntries se WHERE se.Id IN ($seIds)
"@
$srcById = @{}; foreach ($s in $src) { $srcById[[int]$s.Id] = $s }
$shapeProblems = New-Object System.Collections.ArrayList
$shiftMismatch = New-Object System.Collections.ArrayList
foreach ($t in $target) {
    $se = $srcById[[int]$t.SeId]
    if (-not $se) { [void]$shapeProblems.Add("ShiftEntries $($t.SeId) missing"); continue }
    if ($se.ShiftType -ne "WIC_DUTY") { [void]$shapeProblems.Add("$($t.SeId) ShiftType=$($se.ShiftType)") }
    if (-not $se.IsWicDuty) { [void]$shapeProblems.Add("$($t.SeId) IsWicDuty=$($se.IsWicDuty)") }
    if ([string]::IsNullOrWhiteSpace($se.ManualChangeBy)) { [void]$shapeProblems.Add("$($t.SeId) ManualChangeBy empty") }
    if ($se.EmployeeId -ne $t.EmpId -or $se.D -ne $t.Date) { [void]$shapeProblems.Add("$($t.SeId) employee/date changed") }
    $expectedShift = "$($se.ShiftStart)-$($se.ShiftEnd)"
    if ($expectedShift -ne $t.Shift) { [void]$shiftMismatch.Add("$($t.SeId): plan='$($t.Shift)' ShiftEntries='$expectedShift'") }
}
P ("  C2b partner ShiftEntries rows still manual WIC_DUTY : {0} problems   (must be 0)" -f $shapeProblems.Count)
foreach ($s in $shapeProblems) { P ("      PROBLEM: $s") }
if ($shapeProblems.Count -gt 0) { [void]$fail.Add("a partner ShiftEntries row changed shape") }
P ("  C2b plan WorkingShift == ShiftStart-ShiftEnd          : {0} mismatches   (must be 0)" -f $shiftMismatch.Count)
foreach ($s in $shiftMismatch) { P ("      MISMATCH: $s") }
if ($shiftMismatch.Count -gt 0) { [void]$fail.Add("plan WorkingShift differs from the ShiftEntries times") }

# C3: backup
$vc = "C:\ProgramData\PulseRollback\wic-backfill-20261012-162439\WicShiftEntries.csv"
if ([string]::IsNullOrWhiteSpace($BackupCsv)) { $BackupCsv = $vc }
P ""
if (Test-Path -LiteralPath $BackupCsv) {
    $fi = Get-Item -LiteralPath $BackupCsv
    $csvLines = Get-Content -LiteralPath $BackupCsv -Encoding UTF8
    $dataRows = $csvLines.Count - 1
    $maxId = ($csvLines[1..($csvLines.Count-1)] | ForEach-Object { if ($_ -match '^"(\d+)"') { [int]$Matches[1] } } | Measure-Object -Maximum).Maximum
    P ("  C3 backup {0}" -f $BackupCsv)
    P ("      exists={0}  bytes={1}  lastWrite={2}" -f $true, $fi.Length, $fi.LastWriteTime)
    P ("      data rows={0}   (must be 23981)   max Id={1}" -f $dataRows, $maxId)
    if ($dataRows -ne 23981) { [void]$fail.Add("backup CSV has $dataRows data rows, expected 23981") }
} else {
    P ("  C3 backup MISSING: {0}" -f $BackupCsv)
    [void]$fail.Add("backup CSV missing")
}

# C4: live table state
$liveCount = [int](Invoke-Scalar "SELECT COUNT(*) FROM WicShiftEntries")
$liveMax   = [int](Invoke-Scalar "SELECT MAX(Id) FROM WicShiftEntries")
$pairWic   = [int](Invoke-Scalar "SELECT COUNT(*) FROM WicShiftEntries w JOIN ShiftEntries se ON se.EmployeeId=w.EmployeeId AND se.ShiftDate=w.ShiftDate")
P ""
P ("  C4 live WicShiftEntries count = {0}   max Id = {1}" -f $liveCount, $liveMax)
P ("     count + 21 will be {0}" -f ($liveCount + 21))
P ("     rows with a paired ShiftEntries row: {0} of {1} ({2} have none - legacy)" -f $pairWic, $liveCount, ($liveCount - $pairWic))

# C5: the 21 rows the script will write, one line each
P ""
P "=== THE 21 ROWS TO BE WRITTEN (as re-verified now) ==="
P ("  {0,-6} {1,-8} {2,-10} {3,-24} {4,-13} {5,-8} {6}" -f "seId","emp","date","SupportLocation","WorkingShift","day","centre")
P ("  {0,-6} {1,-8} {2,-10} {3,-24} {4,-13} {5,-8} {6}" -f ("-"*6),("-"*8),("-"*10),("-"*24),("-"*13),("-"*8),("-"*8))
foreach ($t in $target) {
    $se = $srcById[[int]$t.SeId]
    P ("  {0,-6} {1,-8} {2,-10} {3,-24} {4,-13} {5,-8} {6}" -f $t.SeId, $t.EmpId, $t.Date, $t.Centre, $t.Shift, ([datetime]::ParseExact($t.Date,'yyyy-MM-dd',$null)).DayOfWeek, $se.AgentTask)
}

$conn.Close()

P ""
P "=== GATE RESULT ==="
if ($fail.Count -eq 0) {
    P "  PASS - cleared for -Apply (21 INSERT, no collisions, backup present with 23981 rows)."
} else {
    P "  STOP - the following checks failed:"
    foreach ($f in $fail) { P ("    * $f") }
}

$text = ($out -join "`r`n")
if (-not [string]::IsNullOrWhiteSpace($OutFile)) { $text | Set-Content -Path $OutFile -Encoding UTF8 }
$text
if ($fail.Count -gt 0) { exit 2 }
