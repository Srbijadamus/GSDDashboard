# Apply-HalWic.ps1  —  RTM 2026-10-15 HAL/WIC task, points A2 and B4.
#
# READ-ONLY BY DEFAULT. Without -Apply this script performs SELECTs only and prints exactly
# what it would change. Writes happen only with an explicit -Apply.
#
# A2 (urgent, 2026-10-16):
#   * Senthuran (EmployeeId looked up by name): ShiftEntries becomes HALF_AL with the
#     WORKING part from the start of his shift to the centre's closing time (13:00), and his
#     Helmstedt WicShiftEntries row becomes on-site with WorkingShift = the intersection of
#     that working part and the centre's window.
#   * Merlin (SL on 2026-10-16): any WIC row for Helmstedt that day is deleted (SL wins).
#
# B4 (stale centred WIC rows from today onwards):
#   * every ON-SITE WicShiftEntries row whose same-day ShiftEntries was manually changed by
#     the RTM (ManualChangeBy = S60028) to a non-WIC type; and
#   * the explicitly named Binod / Pfaffenhofen November rows.
#   Everything else that looks similar is only LISTED (StaleCentredWic-OtherCases.csv).
#
# Rules honoured here:
#   * ManualChangeBy is NEVER set by this script and the RTM KID (S60028) is never written.
#     Every ShiftEntries row this script touches is stamped SourceModule='RTM_RULE 2026-10-15'.
#   * Backup + rollback.sql are written to C:\ProgramData\PulseRollback\hal-wic-<timestamp>\.
#
#   dry run:  pwsh -File Tools\HalWic-20261015\Apply-HalWic.ps1
#   apply:    pwsh -File Tools\HalWic-20261015\Apply-HalWic.ps1 -Apply

[CmdletBinding()]
param(
    [string] $ConnectionString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;",
    [string] $RollbackRoot = 'C:\ProgramData\PulseRollback',
    [string] $Timestamp = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$MARKER   = 'RTM_RULE 2026-10-15'
$isDryRun = -not $Apply
$mode     = if ($isDryRun) { 'DRY RUN (SELECT only, no writes)' } else { 'APPLY (writes)' }

# Centre closing time on the target date, from the live WicOpeningHours row (never guessed).
$TARGET_DATE = '2026-10-16'

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
        $h = [ordered]@{}
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
function Invoke-Scalar([string]$sql, [hashtable]$params = @{}) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
    foreach ($k in $params.Keys) {
        $p = $cmd.CreateParameter(); $p.ParameterName = $k
        $p.Value = if ($null -eq $params[$k]) { [System.DBNull]::Value } else { $params[$k] }
        [void]$cmd.Parameters.Add($p)
    }
    return $cmd.ExecuteScalar()
}
function Sql-Lit($v) {
    if ($null -eq $v) { return 'NULL' }
    if ($v -is [bool]) { return $(if ($v) { '1' } else { '0' }) }
    if ($v -is [int] -or $v -is [long] -or $v -is [decimal] -or $v -is [double]) { return [string]$v }
    if ($v -is [datetime]) { return "'" + $v.ToString('yyyy-MM-ddTHH:mm:ss.fffffff') + "'" }
    return "N'" + ([string]$v).Replace("'", "''") + "'"
}

Write-Host "=== Apply-HalWic.ps1 — $mode ==="
Write-Host "Server today: $(Invoke-Scalar 'SELECT CONVERT(varchar(10), CAST(GETDATE() AS date), 23)')"

# ── Resolve the two employees by name (the report uses EmployeeId only) ────────────────
$sent = Invoke-Rows "SELECT EmployeeId, FullName FROM Employees WHERE FullName LIKE '%Senthuran%' AND IsActive = 1"
$merl = Invoke-Rows "SELECT EmployeeId, FullName FROM Employees WHERE FullName LIKE '%Merlin%' AND IsActive = 1"
$bino = Invoke-Rows "SELECT EmployeeId, FullName FROM Employees WHERE FullName LIKE '%Binod%' AND IsActive = 1"
if ($sent.Count -ne 1) { throw "Expected exactly one active Senthuran, found $($sent.Count)" }
if ($merl.Count -ne 1) { throw "Expected exactly one active Merlin, found $($merl.Count)" }
if ($bino.Count -ne 1) { throw "Expected exactly one active Binod, found $($bino.Count)" }
$sentId = $sent[0].EmployeeId
$merlId = $merl[0].EmployeeId
$binoId = $bino[0].EmployeeId
Write-Host "Employees resolved: Senthuran=$sentId  Merlin=$merlId  Binod=$binoId"

# ── A2 inputs ──────────────────────────────────────────────────────────────────────────
$sentShift = Invoke-Rows @"
SELECT se.Id, se.EmployeeId, se.ShiftDate, se.RawValue, se.ShiftType, se.ShiftStart, se.ShiftEnd,
       se.IsWicDuty, se.SourceSheet, se.AgentTask, se.LocationId, se.AssignmentStatus, se.AutoGenerated,
       se.SourceModule, se.SourceId, se.PreviousStatus, se.ManualChangeBy, se.ManualChangeAt, se.ManualChangeNote
FROM ShiftEntries se WHERE se.EmployeeId = @emp AND se.ShiftDate = CONVERT(date, @d, 23)
"@ @{ emp = $sentId; d = $TARGET_DATE }
if ($sentShift.Count -ne 1) { throw "Senthuran: expected exactly one ShiftEntries row on $TARGET_DATE, found $($sentShift.Count)" }
$sentShift = $sentShift[0]

$centre = Invoke-Rows @"
SELECT l.LocationCode, l.DisplayName, l.LocationCodeLegacy
FROM WicLocations l WHERE l.IsActive = 1 AND l.DisplayName = 'Helmstedt'
"@
if ($centre.Count -ne 1) { throw "Expected exactly one active Helmstedt centre, found $($centre.Count)" }
$centreCode = $centre[0].LocationCode
$centreName = $centre[0].DisplayName

$hours = Invoke-Rows @"
SELECT h.OpenTime, h.CloseTime, h.OpenTime2, h.CloseTime2, h.IsClosed, h.ExceptionDate, h.DayOfWeek, h.EffectiveFrom
FROM WicOpeningHours h
WHERE h.LocationCode = @code AND (h.ExceptionDate = CONVERT(date, @d, 23)
      OR (h.ExceptionDate IS NULL AND h.DayOfWeek = DATEPART(dw, CONVERT(date, @d, 23)) - 1))
ORDER BY CASE WHEN h.ExceptionDate IS NOT NULL THEN 0 ELSE 1 END, h.EffectiveFrom DESC
"@ @{ code = $centreCode; d = $TARGET_DATE }
if ($hours.Count -lt 1) { throw "No opening hours resolved for $centreName on $TARGET_DATE" }
$h = $hours[0]
if ($h.IsClosed) { throw "$centreName is CLOSED on $TARGET_DATE — stop and re-check." }
Write-Host "Centre $centreName on ${TARGET_DATE}: $($h.OpenTime)-$($h.CloseTime) (day-of-week $($h.DayOfWeek))"

# The working part = shift start .. the centre's closing time, and never past the shift end.
$shiftStart = $sentShift.ShiftStart
if ([string]::IsNullOrWhiteSpace($shiftStart)) { throw "Senthuran's row has no ShiftStart — the working part cannot be derived." }
$centreClose = $h.CloseTime
if ([string]::IsNullOrWhiteSpace($centreClose)) { throw "No closing time resolved for $centreName on $TARGET_DATE" }

function To-Min([string]$t) { $p = $t.Split(':'); return ([int]$p[0]) * 60 + [int]$p[1] }
function To-Hhmm([int]$m) { return ('{0:00}:{1:00}' -f [math]::Floor($m / 60), ($m % 60)) }

$workStartMin = To-Min $shiftStart
$centreCloseMin = To-Min $centreClose
$shiftEndMin = if ([string]::IsNullOrWhiteSpace($sentShift.ShiftEnd)) { $centreCloseMin } else { To-Min $sentShift.ShiftEnd }
$workEndMin = [math]::Min($centreCloseMin, $shiftEndMin)
if ($workEndMin -le $workStartMin) { throw "Empty working part ($shiftStart .. $(To-Hhmm $workEndMin)) — stop." }
$workStart = To-Hhmm $workStartMin
$workEnd   = To-Hhmm $workEndMin
# Intersection of the working part and the centre window.
$centreOpenMin = To-Min $h.OpenTime
$ixStart = [math]::Max($workStartMin, $centreOpenMin)
$ixEnd   = [math]::Min($workEndMin, $centreCloseMin)
if ($ixEnd -le $ixStart) { throw "Working part and centre window do not overlap — stop." }
$workingShift = "$(To-Hhmm $ixStart)-$(To-Hhmm $ixEnd)"

Write-Host "A2 Senthuran: ShiftEntries #$($sentShift.Id) [$($sentShift.ShiftType)] $shiftStart-$($sentShift.ShiftEnd) -> HALF_AL working part $workStart-$workEnd ; WIC WorkingShift=$workingShift"

$sentWic = Invoke-Rows @"
SELECT w.Id, w.EmployeeId, w.ShiftDate, w.DayOfWeek, w.SupportLocation, w.WicOpeningHours, w.WorkingShift,
       w.IsOnSite, w.IsGSDDay, w.IsOffDay, w.Task, w.LocationCode
FROM WicShiftEntries w
WHERE w.EmployeeId = @emp AND w.ShiftDate = CONVERT(date, @d, 23) AND w.SupportLocation = @loc
"@ @{ emp = $sentId; d = $TARGET_DATE; loc = $centreName }
if ($sentWic.Count -ne 1) { throw "Senthuran: expected exactly one $centreName WIC row on $TARGET_DATE, found $($sentWic.Count)" }
$sentWic = $sentWic[0]
Write-Host "A2 Senthuran WIC row #$($sentWic.Id): IsOnSite=$($sentWic.IsOnSite) WorkingShift=$($sentWic.WorkingShift) -> IsOnSite=1 WorkingShift=$workingShift"

# Merlin: any Helmstedt WIC row for 2026-10-16 (SL has priority).
$merlWic = Invoke-Rows @"
SELECT w.Id, w.EmployeeId, w.ShiftDate, w.DayOfWeek, w.SupportLocation, w.WicOpeningHours, w.WorkingShift,
       w.IsOnSite, w.IsGSDDay, w.IsOffDay, w.Task, w.LocationCode
FROM WicShiftEntries w
WHERE w.EmployeeId = @emp AND w.ShiftDate = CONVERT(date, @d, 23) AND w.SupportLocation = 'Helmstedt'
ORDER BY w.Id
"@ @{ emp = $merlId; d = $TARGET_DATE }
Write-Host "A2 Merlin WIC rows for Helmstedt $TARGET_DATE : $($merlWic.Count)"
foreach ($r in $merlWic) { Write-Host "    #$($r.Id) IsOnSite=$($r.IsOnSite) WorkingShift=$($r.WorkingShift)" }

# ── B4 delete sets ─────────────────────────────────────────────────────────────────────
$B4_COLS = 'Id, EmployeeId, ShiftDate, DayOfWeek, SupportLocation, WicOpeningHours, WorkingShift, IsOnSite, IsGSDDay, IsOffDay, Task, LocationCode'
$b4OnSite = Invoke-Rows @"
SELECT w.Id, w.EmployeeId, w.ShiftDate, w.DayOfWeek, w.SupportLocation, w.WicOpeningHours, w.WorkingShift,
       w.IsOnSite, w.IsGSDDay, w.IsOffDay, w.Task, w.LocationCode,
       se.Id AS ShiftEntryId, se.ShiftType, se.ManualChangeBy, se.ManualChangeAt
FROM WicShiftEntries w
JOIN ShiftEntries se ON se.EmployeeId = w.EmployeeId AND se.ShiftDate = w.ShiftDate
WHERE w.ShiftDate >= CONVERT(date, '2026-10-15', 23)
  AND w.IsOnSite = 1
  AND se.ShiftType <> 'WIC_DUTY'
  AND se.ManualChangeBy = 'S60028'
ORDER BY w.ShiftDate, w.Id
"@
$b4Binod = Invoke-Rows @"
SELECT w.Id, w.EmployeeId, w.ShiftDate, w.DayOfWeek, w.SupportLocation, w.WicOpeningHours, w.WorkingShift,
       w.IsOnSite, w.IsGSDDay, w.IsOffDay, w.Task, w.LocationCode,
       se.Id AS ShiftEntryId, se.ShiftType, se.ManualChangeBy
FROM WicShiftEntries w
JOIN ShiftEntries se ON se.EmployeeId = w.EmployeeId AND se.ShiftDate = w.ShiftDate
WHERE w.EmployeeId = @emp AND w.SupportLocation = 'Pfaffenhofen'
  AND w.ShiftDate BETWEEN CONVERT(date, '2026-11-01', 23) AND CONVERT(date, '2026-11-30', 23)
  AND se.ShiftType <> 'WIC_DUTY'
ORDER BY w.ShiftDate, w.Id
"@ @{ emp = $binoId }
$b4Ids = @($b4OnSite.Id) + @($b4Binod.Id)
Write-Host "B4 delete: on-site (S60028 non-WIC) = $($b4OnSite.Count); Binod/Pfaffenhofen November = $($b4Binod.Count); total = $($b4Ids.Count)"
if (($b4Ids | Select-Object -Unique).Count -ne $b4Ids.Count) { throw 'B4 delete sets overlap — stop.' }

# The "other cases" list: every centred WIC row whose same-day ShiftEntries is not WIC duty,
# but which is NOT in the delete set. Reported, never written.
$other = Invoke-Rows @"
SELECT w.Id, w.EmployeeId, e.FullName, w.ShiftDate, w.SupportLocation, w.IsOnSite, w.WorkingShift,
       se.Id AS ShiftEntryId, se.ShiftType, se.AgentTask, se.SourceModule, se.ManualChangeBy
FROM WicShiftEntries w
JOIN Employees e ON e.EmployeeId = w.EmployeeId
JOIN ShiftEntries se ON se.EmployeeId = w.EmployeeId AND se.ShiftDate = w.ShiftDate
WHERE se.ShiftDate >= CONVERT(date, '2026-10-15', 23)
  AND se.ShiftType <> 'WIC_DUTY'
  AND w.SupportLocation IS NOT NULL AND w.SupportLocation <> ''
ORDER BY se.ShiftDate, w.EmployeeId, w.Id
"@
$a2Ids = @($sentWic.Id) + @($merlWic.Id)
$otherNotDeleted = $other | Where-Object { $b4Ids -notcontains $_.Id -and $a2Ids -notcontains $_.Id }
Write-Host "B4 list-only (centred, non-WIC shift, NOT deleted and NOT an A2 row) = $($otherNotDeleted.Count)"

if ($isDryRun) {
    Write-Host ''
    Write-Host '--- B4 DELETE: on-site, manual S60028 change, non-WIC ---'
    $b4OnSite | Format-Table Id, EmployeeId, ShiftDate, SupportLocation, WorkingShift, ShiftType | Out-String -Width 220
    Write-Host '--- B4 DELETE: Binod / Pfaffenhofen November ---'
    $b4Binod | Format-Table Id, EmployeeId, ShiftDate, SupportLocation, WorkingShift, ShiftType | Out-String -Width 220
    Write-Host 'DRY RUN complete — nothing was written. Re-run with -Apply to write.'
    $conn.Close()
    return
}

# ── Backup + rollback.sql ──────────────────────────────────────────────────────────────
$dir = Join-Path $RollbackRoot "hal-wic-$Timestamp"
if (Test-Path -LiteralPath $dir) { throw "Rollback directory already exists: $dir" }
New-Item -ItemType Directory -Path $dir | Out-Null

$before = [ordered]@{
    GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Marker         = $MARKER
    TargetDate     = $TARGET_DATE
    Senthuran      = @{ EmployeeId = $sentId; ShiftEntry = $sentShift; WicEntry = $sentWic; NewWorkingShift = $workingShift; NewWorkStart = $workStart; NewWorkEnd = $workEnd }
    Merlin         = @{ EmployeeId = $merlId; WicRows = $merlWic }
    B4OnSite       = $b4OnSite
    B4Binod        = $b4Binod
}
$before | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $dir 'before.json') -Encoding UTF8
$b4OnSite | Export-Csv -LiteralPath (Join-Path $dir 'b4-onsite.csv') -NoTypeInformation -Encoding UTF8
$b4Binod  | Export-Csv -LiteralPath (Join-Path $dir 'b4-binod.csv') -NoTypeInformation -Encoding UTF8
$otherNotDeleted | Export-Csv -LiteralPath (Join-Path $dir 'StaleCentredWic-OtherCases.csv') -NoTypeInformation -Encoding UTF8

$rb = New-Object System.Text.StringBuilder
[void]$rb.AppendLine("-- Rollback for the 2026-10-15 HAL/WIC data change ($MARKER).")
[void]$rb.AppendLine("-- Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') from the before-images read in the same run.")
[void]$rb.AppendLine("-- Restores ShiftEntries #$($sentShift.Id) exactly and re-inserts every WIC row this run deleted.")
[void]$rb.AppendLine('SET XACT_ABORT ON;')
[void]$rb.AppendLine('BEGIN TRAN;')
[void]$rb.AppendLine('')
[void]$rb.AppendLine("-- A2: Senthuran ShiftEntries #$($sentShift.Id)")
[void]$rb.AppendLine("UPDATE ShiftEntries SET RawValue=$(Sql-Lit $sentShift.RawValue), ShiftType=$(Sql-Lit $sentShift.ShiftType), ShiftStart=$(Sql-Lit $sentShift.ShiftStart), ShiftEnd=$(Sql-Lit $sentShift.ShiftEnd), IsWicDuty=$(Sql-Lit $sentShift.IsWicDuty), SourceSheet=$(Sql-Lit $sentShift.SourceSheet), AgentTask=$(Sql-Lit $sentShift.AgentTask), LocationId=$(Sql-Lit $sentShift.LocationId), AssignmentStatus=$(Sql-Lit $sentShift.AssignmentStatus), AutoGenerated=$(Sql-Lit $sentShift.AutoGenerated), SourceModule=$(Sql-Lit $sentShift.SourceModule), SourceId=$(Sql-Lit $sentShift.SourceId), PreviousStatus=$(Sql-Lit $sentShift.PreviousStatus), ManualChangeBy=$(Sql-Lit $sentShift.ManualChangeBy), ManualChangeAt=$(Sql-Lit $sentShift.ManualChangeAt), ManualChangeNote=$(Sql-Lit $sentShift.ManualChangeNote) WHERE Id=$($sentShift.Id);")
[void]$rb.AppendLine("-- A2: Senthuran WicShiftEntries #$($sentWic.Id)")
[void]$rb.AppendLine("UPDATE WicShiftEntries SET SupportLocation=$(Sql-Lit $sentWic.SupportLocation), WicOpeningHours=$(Sql-Lit $sentWic.WicOpeningHours), WorkingShift=$(Sql-Lit $sentWic.WorkingShift), IsOnSite=$(Sql-Lit $sentWic.IsOnSite), IsGSDDay=$(Sql-Lit $sentWic.IsGSDDay), IsOffDay=$(Sql-Lit $sentWic.IsOffDay), Task=$(Sql-Lit $sentWic.Task), LocationCode=$(Sql-Lit $sentWic.LocationCode) WHERE Id=$($sentWic.Id);")

function Add-RollbackInsert([System.Text.StringBuilder]$sb, $row, [string]$comment) {
    [void]$sb.AppendLine("$comment")
    [void]$sb.AppendLine("SET IDENTITY_INSERT WicShiftEntries ON;")
    [void]$sb.AppendLine("INSERT INTO WicShiftEntries ($B4_COLS) VALUES ($(Sql-Lit $row.Id), $(Sql-Lit $row.EmployeeId), $(Sql-Lit $row.ShiftDate), $(Sql-Lit $row.DayOfWeek), $(Sql-Lit $row.SupportLocation), $(Sql-Lit $row.WicOpeningHours), $(Sql-Lit $row.WorkingShift), $(Sql-Lit $row.IsOnSite), $(Sql-Lit $row.IsGSDDay), $(Sql-Lit $row.IsOffDay), $(Sql-Lit $row.Task), $(Sql-Lit $row.LocationCode));")
    [void]$sb.AppendLine("SET IDENTITY_INSERT WicShiftEntries OFF;")
}
foreach ($r in $merlWic) {
    Add-RollbackInsert $rb $r "-- A2: Merlin Helmstedt WIC row #$($r.Id) ($($r.ShiftDate.ToString('yyyy-MM-dd')))"
}
foreach ($r in $b4OnSite) {
    Add-RollbackInsert $rb $r "-- B4: on-site stale WIC row #$($r.Id) (emp $($r.EmployeeId), $($r.ShiftDate.ToString('yyyy-MM-dd')), shift was $($r.ShiftType))"
}
foreach ($r in $b4Binod) {
    Add-RollbackInsert $rb $r "-- B4: Binod/Pfaffenhofen #$($r.Id) ($($r.ShiftDate.ToString('yyyy-MM-dd')))"
}
[void]$rb.AppendLine('')
[void]$rb.AppendLine('COMMIT;')
$rb.ToString() | Set-Content -LiteralPath (Join-Path $dir 'rollback.sql') -Encoding UTF8
Write-Host "Backup + rollback.sql written to $dir"

# ── Apply, in one transaction ──────────────────────────────────────────────────────────
$tx = $conn.BeginTransaction()
try {
    $cmd = $conn.CreateCommand(); $cmd.Transaction = $tx; $cmd.CommandTimeout = 300

    # A2 Senthuran ShiftEntries: HALF_AL working part + marker, NO ManualChangeBy.
    $cmd.CommandText = @"
UPDATE ShiftEntries
SET ShiftStart = @ws, ShiftEnd = @we, SourceModule = @marker,
    ManualChangeBy = NULL, ManualChangeAt = NULL, ManualChangeNote = NULL
WHERE Id = @id
"@
    [void]$cmd.Parameters.AddWithValue('@ws', $workStart)
    [void]$cmd.Parameters.AddWithValue('@we', $workEnd)
    [void]$cmd.Parameters.AddWithValue('@marker', $MARKER)
    [void]$cmd.Parameters.AddWithValue('@id', $sentShift.Id)
    $n1 = $cmd.ExecuteNonQuery(); $cmd.Parameters.Clear()

    # A2 Senthuran WIC row: on site, intersection window.
    $cmd.CommandText = @"
UPDATE WicShiftEntries
SET SupportLocation = @loc, LocationCode = @code, WorkingShift = @ws,
    IsOnSite = 1, IsGSDDay = 0, IsOffDay = 0, Task = 'WIC'
WHERE Id = @id
"@
    [void]$cmd.Parameters.AddWithValue('@loc', $centreName)
    [void]$cmd.Parameters.AddWithValue('@code', $centreCode)
    [void]$cmd.Parameters.AddWithValue('@ws', $workingShift)
    [void]$cmd.Parameters.AddWithValue('@id', $sentWic.Id)
    $n2 = $cmd.ExecuteNonQuery(); $cmd.Parameters.Clear()

    # A2 Merlin deletion.
    $n3 = 0
    foreach ($r in $merlWic) {
        $cmd.CommandText = 'DELETE FROM WicShiftEntries WHERE Id = @id'
        [void]$cmd.Parameters.AddWithValue('@id', $r.Id)
        $n3 += $cmd.ExecuteNonQuery(); $cmd.Parameters.Clear()
    }

    # B4 deletions.
    $n4 = 0
    foreach ($r in ($b4OnSite + $b4Binod)) {
        $cmd.CommandText = 'DELETE FROM WicShiftEntries WHERE Id = @id'
        [void]$cmd.Parameters.AddWithValue('@id', $r.Id)
        $n4 += $cmd.ExecuteNonQuery(); $cmd.Parameters.Clear()
    }

    $cmd.Dispose()
    $tx.Commit()
    Write-Host "APPLIED: Senthuran shift rows updated=$n1, WIC rows updated=$n2; Merlin WIC rows deleted=$n3; B4 WIC rows deleted=$n4"
}
catch {
    $tx.Rollback()
    throw
}
finally {
    $conn.Close()
}
