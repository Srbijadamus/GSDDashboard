# Apply-RtmFix-20261014.ps1
# =====================================================================
# Applies the RTM fix of 2026-10-14. ONE transaction per point.
#
#   POINT 1 : ShiftEntries EmployeeId='9126883' (WIC_DUTY/WORKING,
#             2026-10-15..2027-02-28) -> Mon-Thu 07:30-16:30, Fri 08:00-17:00.
#             Only rows with ManualChangeBy empty OR SourceModule='SHIFT_FIX 2026-10-14'.
#             Rows with ManualChangeBy='S60028' are never touched.
#             Paired WicShiftEntries.WorkingShift = intersection of the new shift and
#             the resolved centre window (the ShiftMismatchFix rule). Rows whose
#             computed value already equals the stored one are NOT written.
#   POINT 2 : none. 9125519 is verification only; this script never writes to it.
#   POINT 3 : WicShiftEntries INSERT for 9047339 at s-Hertogenbosch, one row per day
#             of 2026-10-14..2026-10-30 that has a WIC_DUTY ShiftEntries row and no
#             on-site WicShiftEntries row (IsOnSite=1, WorkingShift = shift sliced to
#             the centre's window).
#
# Backups: full pre-image rows (CSV), SHA256SUMS.txt and rollback.sql in
#          C:\ProgramData\PulseRollback\rtm-fix-20261014-<time>\
# The RTM KID S60028 is never written by this script.
# =====================================================================
[CmdletBinding()]
param(
    [int] $ExpectedChange1 = 64,
    [int] $ExpectedInsert3 = 13,
    [string] $RollbackRoot = 'C:\ProgramData\PulseRollback'
)
$ErrorActionPreference = 'Stop'
. 'C:\GSDDashboard_secure\Tools\LiveDataTargets.ps1'

$serverDay   = [datetime]'2026-10-14'
$traceModule = 'SHIFT_FIX 2026-10-14'
$protectedKid = 'S60028'
$actorNote   = 'RTM fix 2026-10-14: WIC shift hours corrected (S69307 tool; RTM KID never used).'

function Get-ResolvedHours($allHours, [string]$locationCode, [string]$legacyCode, [datetime]$date) {
    $dow = [int]$date.DayOfWeek
    $ex = $allHours | Where-Object {
        $_.LocationCode -eq $locationCode -or ($legacyCode -and $_.LocationCode -eq $legacyCode)
    } | Where-Object {
        -not [string]::IsNullOrEmpty([string]$_.ExceptionDate) -and ([datetime]$_.ExceptionDate).Date -eq $date.Date
    } | Sort-Object { if ([string]::IsNullOrEmpty([string]$_.ManualChangeAt)) { [datetime]::MinValue } else { [datetime]$_.ManualChangeAt } } -Descending
    if ($ex) { return @($ex)[0] }
    $pat = $allHours | Where-Object {
        $_.LocationCode -eq $locationCode -or ($legacyCode -and $_.LocationCode -eq $legacyCode)
    } | Where-Object {
        [string]::IsNullOrEmpty([string]$_.ExceptionDate) -and
        $_.DayOfWeek -eq $dow -and
        ([string]::IsNullOrEmpty([string]$_.EffectiveFrom) -or ([datetime]$_.EffectiveFrom).Date -le $date.Date)
    } | Sort-Object { if ([string]::IsNullOrEmpty([string]$_.EffectiveFrom)) { [datetime]::MinValue } else { [datetime]$_.EffectiveFrom } } -Descending
    if ($pat) { return @($pat)[0] }
    return $null
}
function Get-DutyWindow($row) {
    if (-not $row -or $row.IsClosed) { return $null }
    $s = @($row.OpenTime, $row.OpenTime2) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -First 1
    $e = @($row.CloseTime2, $row.CloseTime) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -First 1
    if (-not $s -or -not $e) { return $null }
    return @{ Start = ([string]$s).Trim(); End = ([string]$e).Trim() }
}
function Get-Minutes([string]$t) { $p = $t.Split(':'); return ([int]$p[0]) * 60 + ([int]$p[1]) }
function Get-HHmm([int]$m) { return ('{0:00}:{1:00}' -f ([math]::Floor($m / 60)), ($m % 60)) }
function Get-Intersection($aS, $aE, $cS, $cE) {
    $s = [math]::Max((Get-Minutes $aS), (Get-Minutes $cS))
    $e = [math]::Min((Get-Minutes $aE), (Get-Minutes $cE))
    if ($e -le $s) { return $null }
    return @{ Start = (Get-HHmm $s); End = (Get-HHmm $e) }
}
function Get-Sql([string]$t) { return $t.Replace("'", "''") }

$target = Get-LiveDataTargets | Where-Object Name -eq 'GSDDashboard'
$conn = New-Object System.Data.SqlClient.SqlConnection($target.Connection)
$conn.Open()
function Invoke-Q([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
    $r = $cmd.ExecuteReader(); $dt = New-Object System.Data.DataTable; $dt.Load($r); $dt
}
function Invoke-NonQ([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
    return $cmd.ExecuteNonQuery()
}

$locations = Invoke-Q "SELECT Id, LocationCode, DisplayName, LocationCodeLegacy, IsActive FROM WicLocations"
$allHours  = Invoke-Q "SELECT Id, LocationCode, DayOfWeek, OpenTime, CloseTime, OpenTime2, CloseTime2, IsClosed, EffectiveFrom, ExceptionDate, ManualChangeAt FROM WicOpeningHours"
$centreByName = @{}
foreach ($l in $locations) { $centreByName[[string]$l.DisplayName] = $l }

# =====================================================================
# Build the plan (fresh SELECT, the same rules the dry run reported)
# =====================================================================
$t1Rows = Invoke-Q @"
SELECT Id, EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, SourceModule, ManualChangeBy
FROM ShiftEntries
WHERE EmployeeId='9126883' AND ShiftDate BETWEEN '2026-10-15' AND '2027-02-28'
  AND ShiftType IN ('WIC_DUTY','WORKING')
ORDER BY ShiftDate, Id
"@
$t1WicByDate = @{}
foreach ($w in (Invoke-Q "SELECT Id, ShiftDate, SupportLocation, LocationCode, WorkingShift, IsOnSite FROM WicShiftEntries WHERE EmployeeId='9126883' AND ShiftDate BETWEEN '2026-10-14' AND '2027-03-01'")) {
    $k = ([datetime]$w.ShiftDate).ToString('yyyy-MM-dd')
    if (-not $t1WicByDate.ContainsKey($k)) { $t1WicByDate[$k] = @() }
    $t1WicByDate[$k] += $w
}
$p1 = New-Object System.Collections.ArrayList
$p1Wic = New-Object System.Collections.ArrayList
$p1Protected = New-Object System.Collections.ArrayList
foreach ($row in $t1Rows) {
    $d = [datetime]$row.ShiftDate; $dow = [int]$d.DayOfWeek
    if ($dow -ge 1 -and $dow -le 4) { $tS = '07:30'; $tE = '16:30' } elseif ($dow -eq 5) { $tS = '08:00'; $tE = '17:00' } else { continue }
    $mcb = [string]$row.ManualChangeBy; $sm = [string]$row.SourceModule
    $editable = ([string]::IsNullOrWhiteSpace($mcb)) -or ($sm -eq $traceModule)
    if (-not $editable) { $p1Protected.Add($row) | Out-Null; continue }
    if ($row.ShiftStart -eq $tS -and $row.ShiftEnd -eq $tE) { continue }
    $p1.Add([pscustomobject]@{ Id = $row.Id; Date = $d; OldStart = $row.ShiftStart; OldEnd = $row.ShiftEnd; NewStart = $tS; NewEnd = $tE }) | Out-Null
    $wic = @($t1WicByDate[$d.ToString('yyyy-MM-dd')] | Where-Object { $_.IsOnSite }) | Select-Object -First 1
    if ($wic) {
        $loc = $centreByName[[string]$wic.SupportLocation]
        $code = if ($loc) { $loc.LocationCode } else { $wic.LocationCode }
        $legacy = if ($loc) { $loc.LocationCodeLegacy } else { $null }
        $cw = Get-DutyWindow (Get-ResolvedHours $allHours $code $legacy $d)
        if ($cw) {
            $ix = Get-Intersection $tS $tE $cw.Start $cw.End
            if ($ix) {
                $new = "$($ix.Start)-$($ix.End)"
                if ($new -ne $wic.WorkingShift) {
                    $p1Wic.Add([pscustomobject]@{ WicId = $wic.Id; Date = $d; OldShift = $wic.WorkingShift; NewShift = $new; Centre = $wic.SupportLocation }) | Out-Null
                }
            }
        }
    }
}

$nlLoc = $locations | Where-Object { $_.DisplayName -eq 's-Hertogenbosch' } | Select-Object -First 1
if (-not $nlLoc) { throw 's-Hertogenbosch WicLocations row not found - refusing to guess.' }
$t3Rows = Invoke-Q "SELECT Id, ShiftDate, ShiftType, ShiftStart, ShiftEnd, AgentTask FROM ShiftEntries WHERE EmployeeId='9047339' AND ShiftDate BETWEEN '2026-10-14' AND '2026-10-30' ORDER BY ShiftDate, Id"
$t3Wic = Invoke-Q "SELECT Id, ShiftDate, SupportLocation, WorkingShift FROM WicShiftEntries WHERE EmployeeId='9047339' AND ShiftDate BETWEEN '2026-10-14' AND '2026-10-30' AND IsOnSite=1"
$t3Absent = Invoke-Q "SELECT ShiftDate, ShiftType FROM ShiftEntries WHERE EmployeeId='9047339' AND ShiftDate BETWEEN '2026-10-14' AND '2026-10-30' AND ShiftType IN ('AL','SL')"
$p3 = New-Object System.Collections.ArrayList
for ($dt = [datetime]'2026-10-14'; $dt -le [datetime]'2026-10-30'; $dt = $dt.AddDays(1)) {
    $duty = $t3Rows | Where-Object { ([datetime]$_.ShiftDate).Date -eq $dt -and $_.ShiftType -eq 'WIC_DUTY' -and ([string]$_.AgentTask).Trim() -eq 's-Hertogenbosch' } | Select-Object -First 1
    if (-not $duty) { continue }
    if (@($t3Wic | Where-Object { ([datetime]$_.ShiftDate).Date -eq $dt }).Count -gt 0) { continue }
    if (@($t3Absent | Where-Object { ([datetime]$_.ShiftDate).Date -eq $dt }).Count -gt 0) { continue }
    $cw = Get-DutyWindow (Get-ResolvedHours $allHours $nlLoc.LocationCode $nlLoc.LocationCodeLegacy $dt)
    if (-not $cw) { continue }
    $ix = Get-Intersection ([string]$duty.ShiftStart) ([string]$duty.ShiftEnd) $cw.Start $cw.End
    if (-not $ix) { continue }
    $p3.Add([pscustomobject]@{
        Date = $dt; Day = $dt.DayOfWeek.ToString(); EmployeeId = '9047339'
        DutyShiftEntryId = $duty.Id; WorkingShift = "$($ix.Start)-$($ix.End)"
        SupportLocation = 's-Hertogenbosch'; LocationCode = $nlLoc.LocationCode
    }) | Out-Null
}

"PLAN  Point1 ShiftEntries updates : $($p1.Count)   (expected $ExpectedChange1)"
"PLAN  Point1 WicShift updates     : $($p1Wic.Count)"
"PLAN  Point1 protected S60028 rows : $($p1Protected.Count)"
"PLAN  Point3 WicShiftEntries insert: $($p3.Count)   (expected $ExpectedInsert3)"
if ($p1.Count -ne $ExpectedChange1) { throw "Point1 plan changed ($($p1.Count) vs $ExpectedChange1) - refusing to write." }
if ($p3.Count -ne $ExpectedInsert3) { throw "Point3 plan changed ($($p3.Count) vs $ExpectedInsert3) - refusing to write." }

# =====================================================================
# BACKUP + rollback.sql
# =====================================================================
$stamp = Get-Date -Format 'HHmmss'
$dir = Join-Path $RollbackRoot ("rtm-fix-20261014-$stamp")
if (Test-Path -LiteralPath $dir) { throw "Backup directory already exists: $dir" }
New-Item -ItemType Directory -Path $dir -Force | Out-Null
"BACKUP DIR : $dir"

$p1Pre = Invoke-Q ("SELECT * FROM ShiftEntries WHERE Id IN (" + (($p1 | ForEach-Object Id) -join ',') + ") ORDER BY Id")
$p1WicPre = if ($p1Wic.Count) { Invoke-Q ("SELECT * FROM WicShiftEntries WHERE Id IN (" + (($p1Wic | ForEach-Object WicId) -join ',') + ") ORDER BY Id") } else { $null }
$p3Pre = Invoke-Q "SELECT * FROM WicShiftEntries WHERE EmployeeId='9047339' AND ShiftDate BETWEEN '2026-10-14' AND '2026-10-30' ORDER BY ShiftDate, Id"

$p1Pre   | Export-Csv (Join-Path $dir 'ShiftEntries-point1-pre.csv')        -NoTypeInformation -Encoding UTF8
$p3Pre   | Export-Csv (Join-Path $dir 'WicShiftEntries-point3-pre.csv')      -NoTypeInformation -Encoding UTF8
if ($p1WicPre) { $p1WicPre | Export-Csv (Join-Path $dir 'WicShiftEntries-point1-pre.csv') -NoTypeInformation -Encoding UTF8 }
$p1      | Export-Csv (Join-Path $dir 'point1-plan.csv')  -NoTypeInformation -Encoding UTF8
$p3      | Export-Csv (Join-Path $dir 'point3-plan.csv')  -NoTypeInformation -Encoding UTF8

# rollback.sql - NOT executed by this script
$rb = New-Object System.Collections.ArrayList
[void]$rb.Add("-- rollback.sql  -  UNDO of the RTM fix 2026-10-14 (POINT 1 + POINT 3)")
[void]$rb.Add("-- Generated $((Get-Date).ToString('s'))   Source: Apply-RtmFix-20261014.ps1")
[void]$rb.Add("-- NOT executed automatically. Run the whole file in one session.")
[void]$rb.Add("SET XACT_ABORT ON;")
[void]$rb.Add("BEGIN TRAN;")
[void]$rb.Add("")
[void]$rb.Add("-- POINT 3 undo: delete exactly the rows this run inserted (guarded by id AND shape)")
foreach ($r in $p3) {
    $ds = $r.Date.ToString('yyyy-MM-dd')
    [void]$rb.Add("DELETE FROM WicShiftEntries WHERE EmployeeId='9047339' AND ShiftDate='$ds' AND SupportLocation='s-Hertogenbosch' AND IsOnSite=1 AND WorkingShift='$($r.WorkingShift)';")
}
[void]$rb.Add("")
[void]$rb.Add("-- POINT 1 undo: restore ShiftStart/ShiftEnd from the pre-image CSV")
foreach ($r in $p1) {
    [void]$rb.Add("UPDATE ShiftEntries SET ShiftStart='$(Get-Sql $r.OldStart)', ShiftEnd='$(Get-Sql $r.OldEnd)' WHERE Id=$($r.Id) AND ShiftStart='$(Get-Sql $r.NewStart)' AND ShiftEnd='$(Get-Sql $r.NewEnd)';")
}
if ($p1Wic.Count) {
    [void]$rb.Add("")
    [void]$rb.Add("-- POINT 1 WicShiftEntries undo")
    foreach ($r in $p1Wic) {
        [void]$rb.Add("UPDATE WicShiftEntries SET WorkingShift='$(Get-Sql $r.OldShift)' WHERE Id=$($r.WicId) AND WorkingShift='$(Get-Sql $r.NewShift)';")
    }
}
[void]$rb.Add("")
[void]$rb.Add("COMMIT;")
($rb -join "`r`n") | Set-Content -LiteralPath (Join-Path $dir 'rollback.sql') -Encoding UTF8

$hashLines = New-Object System.Collections.ArrayList
foreach ($f in (Get-ChildItem -LiteralPath $dir -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name)) {
    $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
    [void]$hashLines.Add("$h  $($f.Name)")
}
($hashLines -join "`r`n") | Set-Content -LiteralPath (Join-Path $dir 'SHA256SUMS.txt') -Encoding UTF8
"BACKUP FILES: " + ((Get-ChildItem -LiteralPath $dir -File | ForEach-Object Name) -join ', ')

# =====================================================================
# POINT 1 - one transaction
# =====================================================================
$conn2 = New-Object System.Data.SqlClient.SqlConnection($target.Connection)
$conn2.Open()
$tx1 = $conn2.BeginTransaction('Point1')
$updated = 0; $guardSkips = New-Object System.Collections.ArrayList
try {
    foreach ($r in $p1) {
        $cmd = $conn2.CreateCommand(); $cmd.Transaction = $tx1
        $cmd.CommandText = @"
UPDATE ShiftEntries
SET ShiftStart=@s, ShiftEnd=@e, ManualChangeBy=@by, ManualChangeAt=@at, ManualChangeNote=@note
WHERE Id=@id AND ShiftStart=@os AND ShiftEnd=@oe
  AND (ManualChangeBy IS NULL OR ManualChangeBy='' OR SourceModule='$traceModule')
"@
        [void]$cmd.Parameters.AddWithValue('@s', $r.NewStart)
        [void]$cmd.Parameters.AddWithValue('@e', $r.NewEnd)
        [void]$cmd.Parameters.AddWithValue('@by', 'S69307')
        [void]$cmd.Parameters.AddWithValue('@at', [datetime]::Now)
        [void]$cmd.Parameters.AddWithValue('@note', $actorNote)
        [void]$cmd.Parameters.AddWithValue('@id', $r.Id)
        [void]$cmd.Parameters.AddWithValue('@os', [string]$r.OldStart)
        [void]$cmd.Parameters.AddWithValue('@oe', [string]$r.OldEnd)
        $n = $cmd.ExecuteNonQuery()
        if ($n -eq 1) { $updated++ } else { $guardSkips.Add("point1 Id=$($r.Id) guard skipped") | Out-Null }
    }
    foreach ($r in $p1Wic) {
        $cmd = $conn2.CreateCommand(); $cmd.Transaction = $tx1
        $cmd.CommandText = "UPDATE WicShiftEntries SET WorkingShift=@n WHERE Id=@id AND WorkingShift=@o"
        [void]$cmd.Parameters.AddWithValue('@n', $r.NewShift)
        [void]$cmd.Parameters.AddWithValue('@id', $r.WicId)
        [void]$cmd.Parameters.AddWithValue('@o', [string]$r.OldShift)
        $n = $cmd.ExecuteNonQuery()
        if ($n -ne 1) { $guardSkips.Add("point1 wic Id=$($r.WicId) guard skipped") | Out-Null }
    }
    if ($guardSkips.Count -gt 0) { throw ("Guard skips in POINT 1: " + ($guardSkips -join '; ')) }
    $tx1.Commit()
    "POINT 1 committed: ShiftEntries updated=$updated  WicShiftEntries updated=$($p1Wic.Count)  guardSkips=0"
} catch {
    $tx1.Rollback()
    throw "POINT 1 rolled back: $($_.Exception.Message)"
} finally { $tx1.Dispose() }

# =====================================================================
# POINT 3 - one transaction
# =====================================================================
$tx3 = $conn2.BeginTransaction('Point3')
$inserted = 0
try {
    foreach ($r in $p3) {
        $cmd = $conn2.CreateCommand(); $cmd.Transaction = $tx3
        $cmd.CommandText = @"
INSERT INTO WicShiftEntries (EmployeeId, ShiftDate, DayOfWeek, SupportLocation, LocationCode, WorkingShift, IsOnSite, IsGSDDay, IsOffDay, Task)
SELECT @emp, @dt, @dow, @loc, @code, @ws, 1, 0, 0, 'WIC'
WHERE NOT EXISTS (SELECT 1 FROM WicShiftEntries WHERE EmployeeId=@emp AND ShiftDate=@dt AND SupportLocation=@loc)
"@
        [void]$cmd.Parameters.AddWithValue('@emp', $r.EmployeeId)
        [void]$cmd.Parameters.AddWithValue('@dt', $r.Date)
        [void]$cmd.Parameters.AddWithValue('@dow', $r.Day)
        [void]$cmd.Parameters.AddWithValue('@loc', $r.SupportLocation)
        [void]$cmd.Parameters.AddWithValue('@code', $r.LocationCode)
        [void]$cmd.Parameters.AddWithValue('@ws', $r.WorkingShift)
        $n = $cmd.ExecuteNonQuery()
        if ($n -eq 1) { $inserted++ } else { throw "POINT 3: row already existed for $($r.Date.ToString('yyyy-MM-dd'))" }
    }
    $tx3.Commit()
    "POINT 3 committed: WicShiftEntries inserted=$inserted"
} catch {
    $tx3.Rollback()
    throw "POINT 3 rolled back: $($_.Exception.Message)"
} finally { $tx3.Dispose() }
$conn2.Close(); $conn.Close()

""
"ROLLBACK DIR : $dir"
"ROLLBACK SQL : $(Join-Path $dir 'rollback.sql')"
