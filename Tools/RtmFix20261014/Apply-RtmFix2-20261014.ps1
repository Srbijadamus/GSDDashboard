# Apply-RtmFix2-20261014.ps1
# =====================================================================
# RTM fix 2026-10-14, continuation: POINT 4 and POINT 5.
# ONE transaction per point. Backup + rollback.sql per point, NOT executed.
#
#   POINT 4 : 9074345 / 2026-10-29. Another agent is in Wesel that day, so:
#             - DELETE WicShiftEntries 9074345 / Wesel / 2026-10-29
#             - set that day's ShiftEntries.AgentTask to Grafenrheinfeld when it is not.
#   POINT 5 : every IsOnSite=1 WicShiftEntries with ShiftDate >= 2026-10-14 whose
#             WorkingShift leaves the centre's resolved duty window -> set it to
#             intersect(agent ShiftEntries window, centre duty window).
#             Empty intersection -> leave untouched and list for RTM.
#             Human-owned rows (paired ShiftEntries.ManualChangeBy set, or
#             SourceModule='ROLLOUT') are trimmed too but listed separately.
#             ShiftEntries rows are NEVER touched by point 5.
#
# The RTM KID S60028 is never written by this script.
# =====================================================================
[CmdletBinding()]
param(
    [string] $RollbackRoot = 'C:\ProgramData\PulseRollback',
    [string] $From = '2026-10-14',
    [int]    $Point4WicId = 67886,
    [int]    $Point4ShiftId = 57688,
    [string] $Point4Centre = 'Grafenrheinfeld',
    [switch] $PlanOnly
)
$ErrorActionPreference = 'Stop'
. 'C:\GSDDashboard_secure\Tools\LiveDataTargets.ps1'

$target = Get-LiveDataTargets | Where-Object Name -eq 'GSDDashboard'
$conn = New-Object System.Data.SqlClient.SqlConnection($target.Connection)
$conn.Open()
function Q([string]$sql) {
    $c = $conn.CreateCommand(); $c.CommandText = $sql; $c.CommandTimeout = 300
    $d = New-Object System.Data.DataTable; $d.Load($c.ExecuteReader()); ,$d
}
function Sql([string]$t) { if ($null -eq $t) { return 'NULL' }; return "'" + $t.Replace("'", "''") + "'" }

$locations = Q "SELECT Id, LocationCode, DisplayName, LocationCodeLegacy, City, IsActive FROM WicLocations"
$allHours  = Q "SELECT Id, LocationCode, DayOfWeek, OpenTime, CloseTime, OpenTime2, CloseTime2, IsClosed, EffectiveFrom, ExceptionDate, ManualChangeAt FROM WicOpeningHours"

$aliases = @{
    'Essen BP1'='DE_Essen_BP1'; 'Essen TK1'='DE_Essen_TK1'; 'Halle'='DE_Halle'; 'Berlin - Gaussstr'='DE_Berlin_Gauss'
    'Furstenwalde'='DE_Furstenwalde'; 'Munchen'='DE_Munchen'; 'Osnabruck'='DE_Osnabruck'; 'Saarbrucken'='DE_Saarbrucken'
    'Demmin - Am Hanseufer'='DE_Demmin_Hanse'; 'Denbosch'='NL_Denbosch'; 'Augsburg'='DE_Augsburg'; 'Bamberg'='DE_Bamberg'
    'Brokdorf'='DE_Brokdorf'; 'Dortmund'='DE_Dortmund'; 'Emmerthal'='DE_Emmerthal'; 'Essenbach'='DE_Essenbach'
    'Grafenrheinfeld'='DE_Grafenrheinfeld'; 'Hamburg'='DE_Hamburg'; 'Hannover'='DE_Hannover'; 'Helmstedt'='DE_Helmstedt'
    'Neu-Isenburg'='DE_NeuIsenburg'; 'Pfaffenhofen'='PFAFFENHOFEN'; 'Potsdam'='DE_Potsdam'; 'Quickborn'='DE_Quickborn'
    'Regensburg'='DE_Regensburg'; 'Rendsburg'='RENDSBURG'; 'Salzgitter'='DE_Salzgitter'; 'Stade'='DE_Stade'
    'Stadland'='DE_Stadland'; 'Zwolle'='NL_Zwolle'; 'Essen (Bruesseler Pl.)'='DE_Essen_BP1'; 'Essen (Brusseler Pl.)'='DE_Essen_BP1'
    'Essen (ThyssenKrupp)'='DE_Essen_TK1'; 'Demmin (Am Hanseufer)'='DE_Demmin_Hanse'; 'Berlin (Koepenicker)'='DE_Berlin_Kopenick'
    'Berlin (Kopenicker)'='DE_Berlin_Kopenick'; 'Berlin - Kopenicker'='DE_Berlin_Kopenick'; 'Berlin - Gaußstr.'='DE_Berlin_Gauss'
    'Berlin - Bruckenstrasse'='DE_Berlin_Kopenick'; 'Essen_BP1'='DE_Essen_BP1'; 'Essen_TK1'='DE_Essen_TK1'
    'Demmin_Wold'='DE_Demmin_Wold'; 'Demmin_Hanse'='DE_Demmin_Hanse'
}
$aliasCmp = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($k in $aliases.Keys) { $aliasCmp[$k] = $aliases[$k] }
function Resolve-Location([string]$sl) {
    if ([string]::IsNullOrWhiteSpace($sl)) { return $null }
    $hit = $locations | Where-Object { $_.DisplayName -eq $sl -or $_.City -eq $sl } | Select-Object -First 1
    if ($hit) { return $hit }
    $code = $null
    if ($aliasCmp.TryGetValue($sl, [ref]$code)) {
        $hit = $locations | Where-Object { $_.LocationCode -eq $code -or $_.LocationCodeLegacy -eq $code } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    return $null
}
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
        [string]::IsNullOrEmpty([string]$_.ExceptionDate) -and $_.DayOfWeek -eq $dow -and
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
function M([string]$t) { if ([string]::IsNullOrWhiteSpace($t)) { return $null }; $p = $t.Trim().Split(':'); if ($p.Count -lt 2) { return $null }; return ([int]$p[0]) * 60 + ([int]$p[1]) }
function HH([int]$m) { return ('{0:00}:{1:00}' -f ([math]::Floor($m / 60)), ($m % 60)) }
function Window([string]$ws) {
    if ([string]::IsNullOrWhiteSpace($ws)) { return $null }
    $m = [regex]::Matches($ws, '\d{1,2}:\d{2}')
    if ($m.Count -lt 2) { return $null }
    return @{ Start = $m[0].Value; End = $m[$m.Count - 1].Value }
}
function Intersect($a, $b) {
    if (-not $a -or -not $b) { return $null }
    $s = [math]::Max((M $a.Start), (M $b.Start)); $e = [math]::Min((M $a.End), (M $b.End))
    if ($e -le $s) { return $null }
    return @{ Start = (HH $s); End = (HH $e) }
}

# =====================================================================
# POINT 4 plan
# =====================================================================
$p4Wic   = Q "SELECT * FROM WicShiftEntries WHERE Id=$Point4WicId"
$p4Shift = Q "SELECT * FROM ShiftEntries WHERE Id=$Point4ShiftId"
if ($p4Wic.Rows.Count -ne 1) { throw "Point4: WicShiftEntries Id=$Point4WicId not found (or not unique)." }
if ($p4Shift.Rows.Count -ne 1) { throw "Point4: ShiftEntries Id=$Point4ShiftId not found (or not unique)." }
$p4WicRow = $p4Wic.Rows[0]; $p4ShiftRow = $p4Shift.Rows[0]
"POINT4 target Wic : Id=$($p4WicRow.Id) Emp=$($p4WicRow.EmployeeId) Date=$(([datetime]$p4WicRow.ShiftDate).ToString('yyyy-MM-dd')) Loc=$($p4WicRow.SupportLocation) WS=$($p4WicRow.WorkingShift) OnSite=$($p4WicRow.IsOnSite)"
"POINT4 target Shift: Id=$($p4ShiftRow.Id) Emp=$($p4ShiftRow.EmployeeId) Date=$(([datetime]$p4ShiftRow.ShiftDate).ToString('yyyy-MM-dd')) Type=$($p4ShiftRow.ShiftType) AgentTask='$($p4ShiftRow.AgentTask)' MCB=$($p4ShiftRow.ManualChangeBy)"
# The other agent in Wesel that day (the precondition of point 4).
$otherAgents = Q "SELECT DISTINCT EmployeeId FROM WicShiftEntries WHERE ShiftDate='2026-10-29' AND SupportLocation LIKE '%Wesel%' AND IsOnSite=1 AND EmployeeId <> '$($p4WicRow.EmployeeId)'"
"POINT4 other agents in Wesel on 2026-10-29: $($otherAgents.Rows.Count)"
foreach ($o in $otherAgents.Rows) { "  agent $($o.EmployeeId) - Wesel on-site row present" }
$p4ShiftNeedsCentre = [string]::IsNullOrWhiteSpace([string]$p4ShiftRow.AgentTask) -or ([string]$p4ShiftRow.AgentTask -ne $Point4Centre)
"POINT4 ShiftEntries needs AgentTask -> '$Point4Centre': $p4ShiftNeedsCentre"

# =====================================================================
# POINT 5 plan (fresh)
# =====================================================================
$fromDt = [datetime]$From
$rows = Q "SELECT * FROM WicShiftEntries WHERE IsOnSite=1 AND ShiftDate >= '$($fromDt.ToString('yyyy-MM-dd'))' ORDER BY ShiftDate, EmployeeId, Id"
$shifts = Q "SELECT Id, EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, AgentTask, SourceModule, ManualChangeBy FROM ShiftEntries WHERE ShiftDate >= '$($fromDt.ToString('yyyy-MM-dd'))'"
$shiftByKey = @{}
foreach ($s in $shifts) { $shiftByKey["$($s.EmployeeId)|$(([datetime]$s.ShiftDate).ToString('yyyy-MM-dd'))"] = $s }

$p5Plan    = New-Object System.Collections.ArrayList   # rows to update
$p5Manual  = New-Object System.Collections.ArrayList   # human-owned subset
$p5Empty   = New-Object System.Collections.ArrayList   # empty intersection -> RTM list
$p5Unknown = New-Object System.Collections.ArrayList   # centre not resolvable -> RTM list
foreach ($r in $rows) {
    $d = [datetime]$r.ShiftDate
    $loc = Resolve-Location ([string]$r.SupportLocation)
    if (-not $loc) {
        $p5Unknown.Add([pscustomobject]@{ Id = $r.Id; EmpId = $r.EmployeeId; Date = $d.ToString('yyyy-MM-dd'); Centre = $r.SupportLocation; Current = $r.WorkingShift; Reason = 'CENTRE_UNRESOLVED' }) | Out-Null
        continue
    }
    $cw = Get-DutyWindow (Get-ResolvedHours $allHours $loc.LocationCode $loc.LocationCodeLegacy $d)
    $cur = Window ([string]$r.WorkingShift)
    if (-not $cw) {
        $p5Unknown.Add([pscustomobject]@{ Id = $r.Id; EmpId = $r.EmployeeId; Date = $d.ToString('yyyy-MM-dd'); Centre = $r.SupportLocation; Current = $r.WorkingShift; Reason = 'CENTRE_CLOSED_OR_NO_HOURS' }) | Out-Null
        continue
    }
    if (-not $cur) { continue }   # no usable WorkingShift -> nothing that "leaves" a window
    $outside = ((M $cur.Start) -lt (M $cw.Start)) -or ((M $cur.End) -gt (M $cw.End))
    if (-not $outside) { continue }
    $sh = $shiftByKey["$($r.EmployeeId)|$($d.ToString('yyyy-MM-dd'))"]
    $agentWindow = $null
    if ($sh) { $agentWindow = Window ("$($sh.ShiftStart)-$($sh.ShiftEnd)") }
    $correct = Intersect $agentWindow $cw
    $human = $sh -and ((-not [string]::IsNullOrWhiteSpace([string]$sh.ManualChangeBy)) -or ([string]$sh.SourceModule -eq 'ROLLOUT'))
    $rec = [pscustomobject]@{
        Id = $r.Id; EmpId = $r.EmployeeId; Date = $d.ToString('yyyy-MM-dd'); Centre = $r.SupportLocation
        Current = [string]$r.WorkingShift; CentreWindow = "$($cw.Start)-$($cw.End)"
        AgentShift = if ($agentWindow) { "$($agentWindow.Start)-$($agentWindow.End)" } else { '' }
        Correct = if ($correct) { "$($correct.Start)-$($correct.End)" } else { '' }
        HumanOwned = [bool]$human
        ShiftSourceModule = if ($sh) { [string]$sh.SourceModule } else { '' }
        ShiftManualChangeBy = if ($sh) { [string]$sh.ManualChangeBy } else { '' }
    }
    if (-not $correct) { $p5Empty.Add($rec) | Out-Null; continue }
    $p5Plan.Add($rec) | Out-Null
    if ($human) { $p5Manual.Add($rec) | Out-Null }
}

"PLAN Point4 delete WicShiftEntries Id=$Point4WicId : 1"
"PLAN Point4 ShiftEntries Id=$Point4ShiftId set AgentTask='$Point4Centre' : $(if ($p4ShiftNeedsCentre) { 1 } else { 0 })"
"PLAN Point5 updates            : $($p5Plan.Count)"
"PLAN Point5 human-owned subset : $($p5Manual.Count)"
"PLAN Point5 empty intersection : $($p5Empty.Count)"
"PLAN Point5 centre unresolved  : $($p5Unknown.Count)"

if ($PlanOnly) { $conn.Close(); return }

# =====================================================================
# BACKUP dirs
# =====================================================================
$stamp = Get-Date -Format 'HHmmss'
$root = Join-Path $RollbackRoot ("rtm-fix-20261014-$stamp")
if (Test-Path -LiteralPath $root) { throw "Backup directory already exists: $root" }
$p4dir = Join-Path $root 'point4'
$p5dir = Join-Path $root 'point5'
New-Item -ItemType Directory -Path $p4dir -Force | Out-Null
New-Item -ItemType Directory -Path $p5dir -Force | Out-Null

# ---- point 4 backup ----
$p4Wic   | Export-Csv (Join-Path $p4dir 'WicShiftEntries-point4-pre.csv')  -NoTypeInformation -Encoding UTF8
$p4Shift | Export-Csv (Join-Path $p4dir 'ShiftEntries-point4-pre.csv')     -NoTypeInformation -Encoding UTF8
$rb4 = New-Object System.Collections.ArrayList
[void]$rb4.Add("-- rollback.sql - UNDO POINT 4 (RTM fix 2026-10-14)")
[void]$rb4.Add("-- Generated $((Get-Date).ToString('s'))")
[void]$rb4.Add("SET XACT_ABORT ON;")
[void]$rb4.Add("BEGIN TRAN;")
[void]$rb4.Add("-- restore the deleted WicShiftEntries row (only if it is really gone)")
[void]$rb4.Add("SET IDENTITY_INSERT WicShiftEntries ON;")
[void]$rb4.Add("INSERT INTO WicShiftEntries (Id, EmployeeId, ShiftDate, DayOfWeek, SupportLocation, WicOpeningHours, WorkingShift, IsOnSite, IsGSDDay, IsOffDay, Task, LocationCode)")
[void]$rb4.Add("SELECT $($p4WicRow.Id), $(Sql ([string]$p4WicRow.EmployeeId)), '$(([datetime]$p4WicRow.ShiftDate).ToString('yyyy-MM-dd'))', $(Sql ([string]$p4WicRow.DayOfWeek)), $(Sql ([string]$p4WicRow.SupportLocation)), $(Sql ([string]$p4WicRow.WicOpeningHours)), $(Sql ([string]$p4WicRow.WorkingShift)), $([int]$p4WicRow.IsOnSite), $([int]$p4WicRow.IsGSDDay), $([int]$p4WicRow.IsOffDay), $(Sql ([string]$p4WicRow.Task)), $(Sql ([string]$p4WicRow.LocationCode))")
[void]$rb4.Add("WHERE NOT EXISTS (SELECT 1 FROM WicShiftEntries WHERE Id=$($p4WicRow.Id));")
[void]$rb4.Add("SET IDENTITY_INSERT WicShiftEntries OFF;")
[void]$rb4.Add("-- restore ShiftEntries.AgentTask (guarded by the value this run wrote)")
[void]$rb4.Add("UPDATE ShiftEntries SET AgentTask=$(Sql ([string]$p4ShiftRow.AgentTask)) WHERE Id=$($p4ShiftRow.Id) AND AgentTask=$(Sql $Point4Centre);")
[void]$rb4.Add("COMMIT;")
($rb4 -join "`r`n") | Set-Content -LiteralPath (Join-Path $p4dir 'rollback.sql') -Encoding UTF8

# ---- point 5 backup ----
$p5Ids = @($p5Plan | ForEach-Object Id)
$p5Pre = if ($p5Ids.Count) { Q ("SELECT * FROM WicShiftEntries WHERE Id IN (" + ($p5Ids -join ',') + ") ORDER BY Id") } else { New-Object System.Data.DataTable }
$p5Pre | Export-Csv (Join-Path $p5dir 'WicShiftEntries-point5-pre.csv') -NoTypeInformation -Encoding UTF8
$p5Plan   | Export-Csv (Join-Path $p5dir 'point5-plan.csv')   -NoTypeInformation -Encoding UTF8
$p5Manual | Export-Csv (Join-Path $p5dir 'point5-manual.csv') -NoTypeInformation -Encoding UTF8
$p5Empty  | Export-Csv (Join-Path $p5dir 'point5-empty-intersection.csv') -NoTypeInformation -Encoding UTF8
$p5Unknown | Export-Csv (Join-Path $p5dir 'point5-centre-unresolved.csv') -NoTypeInformation -Encoding UTF8
$p4Plan = [pscustomobject]@{ DeleteWicId = $Point4WicId; ShiftId = $Point4ShiftId; OldAgentTask = [string]$p4ShiftRow.AgentTask; NewAgentTask = $Point4Centre; OtherAgentInWesel = ($otherAgents.Rows.Count -gt 0) }
@($p4Plan) | Export-Csv (Join-Path $p4dir 'point4-plan.csv') -NoTypeInformation -Encoding UTF8
$rb5 = New-Object System.Collections.ArrayList
[void]$rb5.Add("-- rollback.sql - UNDO POINT 5 (RTM fix 2026-10-14) - WorkingShift values")
[void]$rb5.Add("-- Generated $((Get-Date).ToString('s'))")
[void]$rb5.Add("SET XACT_ABORT ON;")
[void]$rb5.Add("BEGIN TRAN;")
foreach ($r in $p5Plan) {
    [void]$rb5.Add("UPDATE WicShiftEntries SET WorkingShift=$(Sql ([string]$r.Current)) WHERE Id=$($r.Id) AND WorkingShift=$(Sql ([string]$r.Correct));")
}
[void]$rb5.Add("COMMIT;")
($rb5 -join "`r`n") | Set-Content -LiteralPath (Join-Path $p5dir 'rollback.sql') -Encoding UTF8

foreach ($pair in @(@($p4dir), @($p5dir))) {
    $dir = $pair[0]
    $hashLines = New-Object System.Collections.ArrayList
    foreach ($f in (Get-ChildItem -LiteralPath $dir -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name)) {
        [void]$hashLines.Add("$((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash)  $($f.Name)")
    }
    ($hashLines -join "`r`n") | Set-Content -LiteralPath (Join-Path $dir 'SHA256SUMS.txt') -Encoding UTF8
}
"BACKUP ROOT: $root"

# =====================================================================
# POINT 4 - one transaction
# =====================================================================
$conn2 = New-Object System.Data.SqlClient.SqlConnection($target.Connection)
$conn2.Open()
$tx4 = $conn2.BeginTransaction('Point4')
try {
    $cmd = $conn2.CreateCommand(); $cmd.Transaction = $tx4
    $cmd.CommandText = "DELETE FROM WicShiftEntries WHERE Id=$Point4WicId AND EmployeeId='$($p4WicRow.EmployeeId)' AND ShiftDate='2026-10-29' AND IsOnSite=1"
    $del = $cmd.ExecuteNonQuery()
    $upd = 0
    if ($p4ShiftNeedsCentre) {
        $cmd = $conn2.CreateCommand(); $cmd.Transaction = $tx4
        $cmd.CommandText = "UPDATE ShiftEntries SET AgentTask=@t WHERE Id=$Point4ShiftId AND (AgentTask IS NULL OR AgentTask = '')"
        [void]$cmd.Parameters.AddWithValue('@t', $Point4Centre)
        $upd = $cmd.ExecuteNonQuery()
    }
    if ($del -ne 1) { throw "POINT 4: expected to delete exactly 1 WicShiftEntries row, deleted $del" }
    $tx4.Commit()
    "POINT 4 committed: WicShiftEntries deleted=$del  ShiftEntries updated=$upd"
} catch {
    $tx4.Rollback(); throw "POINT 4 rolled back: $($_.Exception.Message)"
} finally { $tx4.Dispose() }

# =====================================================================
# POINT 5 - one transaction
# =====================================================================
$tx5 = $conn2.BeginTransaction('Point5')
try {
    $updated = 0; $guardSkips = New-Object System.Collections.ArrayList
    foreach ($r in $p5Plan) {
        $cmd = $conn2.CreateCommand(); $cmd.Transaction = $tx5
        $cmd.CommandText = "UPDATE WicShiftEntries SET WorkingShift=@n WHERE Id=@id AND WorkingShift=@o"
        [void]$cmd.Parameters.AddWithValue('@n', [string]$r.Correct)
        [void]$cmd.Parameters.AddWithValue('@id', [int]$r.Id)
        [void]$cmd.Parameters.AddWithValue('@o', [string]$r.Current)
        $n = $cmd.ExecuteNonQuery()
        if ($n -eq 1) { $updated++ } else { $guardSkips.Add($r) | Out-Null }
    }
    $tx5.Commit()
    "POINT 5 committed: WicShiftEntries updated=$updated  guardSkips=$($guardSkips.Count)"
    if ($guardSkips.Count) {
        $guardSkips | Export-Csv (Join-Path $p5dir 'point5-guard-skips.csv') -NoTypeInformation -Encoding UTF8
        foreach ($g in $guardSkips) { "  GUARD SKIP (concurrent change) Id=$($g.Id) Emp=$($g.EmpId) Date=$($g.Date) expected='$($g.Current)'" }
    }
} catch {
    $tx5.Rollback(); throw "POINT 5 rolled back: $($_.Exception.Message)"
} finally { $tx5.Dispose() }
$conn2.Close(); $conn.Close()
""
"ROLLBACK ROOT : $root"
"ROLLBACK P4   : $(Join-Path $p4dir 'rollback.sql')"
"ROLLBACK P5   : $(Join-Path $p5dir 'rollback.sql')"
