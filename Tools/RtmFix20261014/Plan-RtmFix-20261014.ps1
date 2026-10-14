# Plan-RtmFix-20261014.ps1
# =====================================================================
# RTM fix 2026-10-14  (dry run by default; -Apply writes)
#
# Point 1 : 9126883 ShiftEntries WIC_DUTY/WORKING 2026-10-15..2027-02-28
#           Mon-Thu 07:30-16:30, Fri 08:00-17:00.
#           ONLY rows with ManualChangeBy empty OR SourceModule='SHIFT_FIX 2026-10-14'.
#           Rows with ManualChangeBy='S60028' are listed, never touched.
#           Paired WicShiftEntries.WorkingShift = intersection of the new shift and
#           the resolved centre working window for that date (WicHoursResolver.DutyWindow).
# Point 2 : 9125519 - READ ONLY verification report, no writes at all.
#           Rule: 07:30-16:30, except when covering Stade -> 08:00-17:00.
#           List rows >= 2026-10-15 that do NOT match and are not manual (S60028).
# Point 3 : 9047339 (s-Hertogenbosch) 2026-10-14..2026-10-30: for every day with a
#           WIC_DUTY ShiftEntries row at s-Hertogenbosch but no WicShiftEntries row,
#           INSERT one sync row (IsOnSite=1, WorkingShift = shift sliced to centre hours).
#           Skip days with AL/SL or an overlapping on-site row of the same agent.
#           Both 9074512 and 9047339 confirmed by RTM; only 9047339 has gaps.
#
# Backups + rollback.sql go to C:\ProgramData\PulseRollback\rtm-fix-20261014-<time>\
# ONE transaction per point. Nothing is written without -Apply.
# The RTM KID S60028 is never written anywhere by this script.
# =====================================================================
[CmdletBinding()]
param(
    [switch] $Apply,
    [int]    $ExpectedChange1 = -1,
    [int]    $ExpectedInsert3 = -1,
    [string] $OutDir = 'C:\GSDDashboard_secure\.tmp-rtmfix-20261014',
    [string] $RollbackRoot = 'C:\ProgramData\PulseRollback'
)
$ErrorActionPreference = 'Stop'
. (Join-Path 'C:\GSDDashboard_secure\Tools' 'LiveDataTargets.ps1')

$serverDay = [datetime]'2026-10-14'
$traceModule = 'SHIFT_FIX 2026-10-14'
$protectedKid = 'S60028'

# ---------------------------------------------------------------------
# Resolve centre hours exactly like WicHoursResolver (single definition).
# ---------------------------------------------------------------------
function Get-ResolvedHours($allHours, [string]$locationCode, [string]$legacyCode, [datetime]$date) {
    $dow = [int]$date.DayOfWeek
    # NB: a NULL date column arrives as [DBNull], which is TRUTHY in PowerShell, so
    # "-not $row.ExceptionDate" is silently FALSE for every row. Always use IsNullOrEmpty.
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
function Get-Intersection($aStart, $aEnd, $cStart, $cEnd) {
    $s = [math]::Max((Get-Minutes $aStart), (Get-Minutes $cStart))
    $e = [math]::Min((Get-Minutes $aEnd),   (Get-Minutes $cEnd))
    if ($e -le $s) { return $null }
    return @{ Start = (Get-HHmm $s); End = (Get-HHmm $e) }
}

# ---------------------------------------------------------------------
# Data load (one connection, read only until -Apply).
# ---------------------------------------------------------------------
$target = Get-LiveDataTargets | Where-Object Name -eq 'GSDDashboard'
$conn = New-Object System.Data.SqlClient.SqlConnection($target.Connection)
$conn.Open()
function Invoke-Q([string]$sql, $params) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
    if ($params) { foreach ($k in $params.Keys) { [void]$cmd.Parameters.AddWithValue($k, $params[$k]) } }
    $r = $cmd.ExecuteReader(); $dt = New-Object System.Data.DataTable; $dt.Load($r); $dt
}
$locations  = Invoke-Q "SELECT Id, LocationCode, DisplayName, LocationCodeLegacy, IsActive FROM WicLocations"
$allHours   = Invoke-Q "SELECT Id, LocationCode, DayOfWeek, OpenTime, CloseTime, OpenTime2, CloseTime2, IsClosed, EffectiveFrom, ExceptionDate, ManualChangeAt FROM WicOpeningHours"
$centreByName = @{}
foreach ($l in $locations) { $centreByName[[string]$l.DisplayName] = $l }

# =====================================================================
# POINT 1
# =====================================================================
$t1Rows = Invoke-Q @"
SELECT Id, EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, IsWicDuty, LocationId, AgentTask,
       AutoGenerated, SourceModule, ManualChangeBy
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

$p1Change = New-Object System.Collections.ArrayList
$p1WicChange = New-Object System.Collections.ArrayList
$p1Protected = New-Object System.Collections.ArrayList
$p1Noop = 0; $p1Warn = New-Object System.Collections.ArrayList
# Report-only guard: a WIC_DUTY/WORKING row on a date that also carries AL/SL for this
# agent is a pre-existing contradiction RTM must decide on; it is flagged, never blocked.
$t1AbsenceDates = @{}
foreach ($a in (Invoke-Q "SELECT ShiftDate FROM ShiftEntries WHERE EmployeeId='9126883' AND ShiftDate BETWEEN '2026-10-15' AND '2027-02-28' AND ShiftType IN ('AL','SL')")) {
    $t1AbsenceDates[([datetime]$a.ShiftDate).ToString('yyyy-MM-dd')] = $true
}
foreach ($row in $t1Rows) {
    $d = [datetime]$row.ShiftDate
    $dow = [int]$d.DayOfWeek
    if ($dow -ge 1 -and $dow -le 4) { $tgtS = '07:30'; $tgtE = '16:30' }
    elseif ($dow -eq 5)             { $tgtS = '08:00'; $tgtE = '17:00' }
    else { $p1Warn.Add("Point1: weekend row $($row.Id) $($d.ToString('yyyy-MM-dd')) skipped") | Out-Null; continue }

    $mcb = [string]$row.ManualChangeBy
    $sm  = [string]$row.SourceModule
    $isEditable = ([string]::IsNullOrWhiteSpace($mcb)) -or ($sm -eq $traceModule)

    if (-not $isEditable) {
        $p1Protected.Add([pscustomobject]@{
            ShiftEntryId = $row.Id; ShiftDate = $d.ToString('yyyy-MM-dd'); Day = $d.DayOfWeek
            ShiftType = $row.ShiftType; Current = "$($row.ShiftStart)-$($row.ShiftEnd)"
            Target = "$tgtS-$tgtE"; ManualChangeBy = $mcb; SourceModule = $sm
        }) | Out-Null
        continue
    }
    if ($row.ShiftStart -eq $tgtS -and $row.ShiftEnd -eq $tgtE) { $p1Noop++; continue }

    # paired WicShiftEntries -> intersection of new shift and the centre window
    $wicId = $null; $wicOld = $null; $wicNew = $null; $note = ''
    $wicRows = @($t1WicByDate[$d.ToString('yyyy-MM-dd')] | Where-Object { $_.IsOnSite })
    $wic = $wicRows | Select-Object -First 1
    if ($wic) {
        $wicId = $wic.Id; $wicOld = $wic.WorkingShift
        $loc = $centreByName[[string]$wic.SupportLocation]
        $code = if ($loc) { $loc.LocationCode } else { $wic.LocationCode }
        $legacy = if ($loc) { $loc.LocationCodeLegacy } else { $null }
        $h = Get-ResolvedHours $allHours $code $legacy $d
        $cw = Get-DutyWindow $h
        if ($cw) {
            $ix = Get-Intersection $tgtS $tgtE $cw.Start $cw.End
            if ($ix) { $wicNew = "$($ix.Start)-$($ix.End)"; if ($wicNew -ne $wicOld) { $p1WicChange.Add([pscustomobject]@{ WicId=$wicId; ShiftDate=$d.ToString('yyyy-MM-dd'); Old=$wicOld; New=$wicNew; Centre=$wic.SupportLocation }) | Out-Null } else { $note = 'wic already equal' }
            } else { $note = "empty intersection (centre $($wic.SupportLocation) $($cw.Start)-$($cw.End))" }
        } else { $note = "centre $($wic.SupportLocation) closed/no window" }
    } else { $note = 'no on-site WicShiftEntries row' }

    $p1Change.Add([pscustomobject]@{
        ShiftEntryId = $row.Id; ShiftDate = $d.ToString('yyyy-MM-dd'); Day = $d.DayOfWeek
        ShiftType = $row.ShiftType; OldStart = $row.ShiftStart; OldEnd = $row.ShiftEnd
        NewStart = $tgtS; NewEnd = $tgtE; SourceModule = $sm; ManualChangeBy = $mcb
        AutoGenerated = $row.AutoGenerated; WicId = $wicId; WicOld = $wicOld; WicNew = $wicNew
        AlsoHasAbsence = [bool]$t1AbsenceDates[$d.ToString('yyyy-MM-dd')]; Note = $note
    }) | Out-Null
}

# =====================================================================
# POINT 2  (verification only)
# =====================================================================
$t2Rows = Invoke-Q @"
SELECT Id, EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, LocationId, AgentTask,
       SourceModule, ManualChangeBy, AutoGenerated
FROM ShiftEntries
WHERE EmployeeId='9125519' AND ShiftDate >= '2026-10-15' AND ShiftType='WIC_DUTY'
ORDER BY ShiftDate, Id
"@
$p2Ok = New-Object System.Collections.ArrayList
$p2Violation = New-Object System.Collections.ArrayList
$p2Manual = New-Object System.Collections.ArrayList
foreach ($row in $t2Rows) {
    $d = [datetime]$row.ShiftDate
    # ShiftEntries carries the centre NAME in AgentTask (LocationId is NULL for this agent).
    $loc = ([string]$row.AgentTask).Trim()
    if (-not $loc) { $loc = ([string]$row.LocationId).Trim() }
    if ($loc -eq 'Stade') { $expS = '08:00'; $expE = '17:00' } else { $expS = '07:30'; $expE = '16:30' }
    $entry = [pscustomobject]@{
        ShiftEntryId=$row.Id; ShiftDate=$d.ToString('yyyy-MM-dd'); Day=$d.DayOfWeek; Location=$loc
        Current="$($row.ShiftStart)-$($row.ShiftEnd)"; Expected="$expS-$expE"
        SourceModule=$row.SourceModule; ManualChangeBy=$row.ManualChangeBy
    }
    $isManual = -not [string]::IsNullOrWhiteSpace([string]$row.ManualChangeBy)
    if ($isManual) { $p2Manual.Add($entry) | Out-Null }
    elseif ($row.ShiftStart -eq $expS -and $row.ShiftEnd -eq $expE) { $p2Ok.Add($entry) | Out-Null }
    else { $p2Violation.Add($entry) | Out-Null }
}

# =====================================================================
# POINT 3
# =====================================================================
$t3Start = [datetime]'2026-10-14'; $t3End = [datetime]'2026-10-30'
$t3 = Invoke-Q @"
SELECT Id, EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, LocationId, AgentTask, SourceModule
FROM ShiftEntries
WHERE EmployeeId='9047339' AND ShiftDate BETWEEN '2026-10-14' AND '2026-10-30'
ORDER BY ShiftDate, Id
"@
$t3Wic = Invoke-Q "SELECT Id, ShiftDate, SupportLocation, WorkingShift, IsOnSite FROM WicShiftEntries WHERE EmployeeId='9047339' AND ShiftDate BETWEEN '2026-10-14' AND '2026-10-30'"
$t3Absent = Invoke-Q "SELECT ShiftDate, ShiftType FROM ShiftEntries WHERE EmployeeId='9047339' AND ShiftDate BETWEEN '2026-10-14' AND '2026-10-30' AND ShiftType IN ('AL','SL')"
# "overlapping on-site row of the same agent" = any on-site WicShiftEntries row of this
# agent on that date at any centre (also the cross-centre case), resolved via AgentTask name.
$t3OtherOnsite = Invoke-Q "SELECT ShiftDate, SupportLocation, WorkingShift FROM WicShiftEntries WHERE EmployeeId='9047339' AND ShiftDate BETWEEN '2026-10-14' AND '2026-10-30' AND IsOnSite=1"

$nlLoc = $locations | Where-Object { $_.DisplayName -eq 's-Hertogenbosch' } | Select-Object -First 1
if (-not $nlLoc) { throw 's-Hertogenbosch WicLocations row not found - refusing to guess.' }

$p3Insert = New-Object System.Collections.ArrayList
$p3Skip = New-Object System.Collections.ArrayList
for ($dt = $t3Start; $dt -le $t3End; $dt = $dt.AddDays(1)) {
    $key = $dt.ToString('yyyy-MM-dd')
    $duty = $t3 | Where-Object { ([datetime]$_.ShiftDate).Date -eq $dt -and $_.ShiftType -eq 'WIC_DUTY' -and ([string]$_.AgentTask).Trim() -eq 's-Hertogenbosch' } | Select-Object -First 1
    if (-not $duty) { continue }
    $onsite = @($t3Wic | Where-Object { ([datetime]$_.ShiftDate).Date -eq $dt -and $_.IsOnSite })
    if ($onsite.Count -gt 0) { $p3Skip.Add([pscustomobject]@{ Date=$key; Reason="on-site WicShiftEntries row already exists (Id $($onsite[0].Id), $($onsite[0].SupportLocation) $($onsite[0].WorkingShift))" }) | Out-Null; continue }
    $other = @($t3OtherOnsite | Where-Object { ([datetime]$_.ShiftDate).Date -eq $dt })
    if ($other.Count -gt 0) { $p3Skip.Add([pscustomobject]@{ Date=$key; Reason="overlapping on-site row of the same agent ($($other[0].SupportLocation) $($other[0].WorkingShift))" }) | Out-Null; continue }
    $al = $t3Absent | Where-Object { ([datetime]$_.ShiftDate).Date -eq $dt }
    if ($al) { $p3Skip.Add([pscustomobject]@{ Date=$key; Reason="absence $($al[0].ShiftType)" }) | Out-Null; continue }

    $h = Get-ResolvedHours $allHours $nlLoc.LocationCode $nlLoc.LocationCodeLegacy $dt
    $cw = Get-DutyWindow $h
    if (-not $cw) { $p3Skip.Add([pscustomobject]@{ Date=$key; Reason='centre closed / no window' }) | Out-Null; continue }
    $ix = Get-Intersection ([string]$duty.ShiftStart) ([string]$duty.ShiftEnd) $cw.Start $cw.End
    if (-not $ix) { $p3Skip.Add([pscustomobject]@{ Date=$key; Reason="empty intersection (shift $($duty.ShiftStart)-$($duty.ShiftEnd), centre $($cw.Start)-$($cw.End))" }) | Out-Null; continue }
    $p3Insert.Add([pscustomobject]@{
        Date=$key; Day=$dt.DayOfWeek.ToString(); EmployeeId='9047339'; DutyShiftEntryId=$duty.Id
        DutyShift="$($duty.ShiftStart)-$($duty.ShiftEnd)"; WorkingShift="$($ix.Start)-$($ix.End)"
        SupportLocation='s-Hertogenbosch'; LocationCode=$nlLoc.LocationCode
    }) | Out-Null
}

# cross-check: 9074512 must have no gaps in the same window
$t3b = Invoke-Q @"
SELECT se.Id, se.ShiftDate FROM ShiftEntries se
WHERE se.EmployeeId='9074512' AND se.ShiftDate BETWEEN '2026-10-14' AND '2026-10-30' AND se.ShiftType='WIC_DUTY'
  AND se.AgentTask='s-Hertogenbosch'
  AND NOT EXISTS (SELECT 1 FROM WicShiftEntries w WHERE w.EmployeeId=se.EmployeeId AND w.ShiftDate=se.ShiftDate AND w.IsOnSite=1)
"@
$t3bGaps = $t3b.Rows.Count

# =====================================================================
# REPORT
# =====================================================================
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$p1Change      | Export-Csv (Join-Path $OutDir 'p1-changes.csv')        -NoTypeInformation -Encoding UTF8
$p1WicChange   | Export-Csv (Join-Path $OutDir 'p1-wic-changes.csv')    -NoTypeInformation -Encoding UTF8
$p1Protected   | Export-Csv (Join-Path $OutDir 'p1-protected.csv')      -NoTypeInformation -Encoding UTF8
$p2Ok          | Export-Csv (Join-Path $OutDir 'p2-conforming.csv')     -NoTypeInformation -Encoding UTF8
$p2Violation   | Export-Csv (Join-Path $OutDir 'p2-violations.csv')     -NoTypeInformation -Encoding UTF8
$p2Manual      | Export-Csv (Join-Path $OutDir 'p2-manual.csv')         -NoTypeInformation -Encoding UTF8
$p3Insert      | Export-Csv (Join-Path $OutDir 'p3-inserts.csv')        -NoTypeInformation -Encoding UTF8
$p3Skip        | Export-Csv (Join-Path $OutDir 'p3-skips.csv')          -NoTypeInformation -Encoding UTF8

'===== POINT 1 : 9126883 ====='
"  rows in scope (WIC_DUTY/WORKING 2026-10-15..2027-02-28) : $($t1Rows.Rows.Count)"
"  CHANGES  (ShiftStart/ShiftEnd rewrite)                   : $($p1Change.Count)"
"  already correct (no write)                               : $p1Noop"
"  protected, ManualChangeBy=$protectedKid (listed only)     : $($p1Protected.Count)"
"  WicShiftEntries.WorkingShift updates                     : $($p1WicChange.Count)"
""
'  -- changed rows --'
$p1Change | Format-Table ShiftDate,Day,ShiftType,OldStart,OldEnd,NewStart,NewEnd,SourceModule,WicOld,WicNew -AutoSize | Out-String -Width 400
'  -- protected (S60028) --'
$p1Protected | Format-Table ShiftDate,Day,ShiftType,Current,Target,SourceModule -AutoSize | Out-String -Width 400
if ($p1Warn.Count) { '  -- warnings --'; $p1Warn }
""
'===== POINT 2 : 9125519 (verification only, NOTHING is written) ====='
"  WIC_DUTY rows >= 2026-10-15 : $($t2Rows.Rows.Count)"
"  conform to rule             : $($p2Ok.Count)"
"  manually changed (S60028)   : $($p2Manual.Count)"
"  VIOLATIONS (not manual)     : $($p2Violation.Count)"
""
$p2Violation | Format-Table ShiftDate,Day,Location,Current,Expected,SourceModule,ManualChangeBy -AutoSize | Out-String -Width 400
""
'===== POINT 3 : 9047339 s-Hertogenbosch 2026-10-14..2026-10-30 ====='
"  WIC_DUTY s-Hertogenbosch days with a missing WicShiftEntries row : $($p3Insert.Count)"
"  skipped days                                                     : $($p3Skip.Count)"
"  9074512 missing-row cross-check                                  : $t3bGaps"
""
$p3Insert | Format-Table Date,Day,DutyShift,WorkingShift -AutoSize | Out-String -Width 400
$p3Skip   | Format-Table Date,Reason -AutoSize | Out-String -Width 400

"OUTDIR  : $OutDir"
"APPLY   : $Apply"
if ($Apply) {
    if ($p1Change.Count -ne $ExpectedChange1) { throw "Point1 plan is $($p1Change.Count) rows, expected $ExpectedChange1 - refusing to write." }
    if ($p3Insert.Count -ne $ExpectedInsert3) { throw "Point3 plan is $($p3Insert.Count) rows, expected $ExpectedInsert3 - refusing to write." }
    "APPLY NOT IMPLEMENTED IN THIS DRY-RUN REVISION"
}
$conn.Close()
