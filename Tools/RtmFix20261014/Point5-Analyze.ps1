# Point5-Analyze.ps1 - READ ONLY. Reports IsOnSite=1 WicShiftEntries >= 2026-10-14
# whose WorkingShift leaves the centre's resolved duty window.
# Never writes to the database.
[CmdletBinding()]
param(
    [string] $ReportDir = 'C:\GSDDashboard_secure\.tmp-rtmfix2-20261014',
    [string] $From = '2026-10-14'
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

$locations = Q "SELECT Id, LocationCode, DisplayName, LocationCodeLegacy, IsActive FROM WicLocations"
$allHours  = Q "SELECT Id, LocationCode, DayOfWeek, OpenTime, CloseTime, OpenTime2, CloseTime2, IsClosed, EffectiveFrom, ExceptionDate, ManualChangeAt FROM WicOpeningHours"

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

$locByName = @{}
foreach ($l in $locations) { $locByName[[string]$l.DisplayName] = $l }
# Mirror of WicLocationMatcher._aliases (SupportLocation -> legacy code).
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
$aliasesCmp = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($k in $aliases.Keys) { $aliasesCmp[$k] = $aliases[$k] }
function Resolve-Location([string]$sl) {
    if ([string]::IsNullOrWhiteSpace($sl)) { return $null }
    $s = $sl
    $hit = $locations | Where-Object { $_.DisplayName -eq $s -or $_.City -eq $s } | Select-Object -First 1
    if ($hit) { return $hit }
    $code = $null
    if ($aliasesCmp.TryGetValue($s, [ref]$code)) {
        $hit = $locations | Where-Object { $_.LocationCode -eq $code -or $_.LocationCodeLegacy -eq $code } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    return $null
}

$fromDt = [datetime]$From
$rows = Q "SELECT Id, EmployeeId, ShiftDate, SupportLocation, LocationCode, WorkingShift, IsOnSite, IsGSDDay, IsOffDay, Task FROM WicShiftEntries WHERE IsOnSite=1 AND ShiftDate >= '$($fromDt.ToString('yyyy-MM-dd'))' ORDER BY ShiftDate, EmployeeId, Id"
$shifts = Q "SELECT Id, EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, AgentTask, SourceModule, ManualChangeBy FROM ShiftEntries WHERE ShiftDate >= '$($fromDt.ToString('yyyy-MM-dd'))'"
$shiftByKey = @{}
foreach ($s in $shifts) { $shiftByKey["$($s.EmployeeId)|$(([datetime]$s.ShiftDate).ToString('yyyy-MM-dd'))"] = $s }

$out = New-Object System.Collections.ArrayList
foreach ($r in $rows) {
    $d = [datetime]$r.ShiftDate
    $loc = Resolve-Location ([string]$r.SupportLocation)
    if (-not $loc) {
        $out.Add([pscustomobject]@{ Id=$r.Id; EmpId=$r.EmployeeId; Date=$d.ToString('yyyy-MM-dd'); Centre=$r.SupportLocation; Status='UNKNOWN_CENTRE'; Current=$r.WorkingShift; CentreWindow=''; AgentShift=''; Correct=''; HumanOwned='' }) | Out-Null
        continue
    }
    $cw = Get-DutyWindow (Get-ResolvedHours $allHours $loc.LocationCode $loc.LocationCodeLegacy $d)
    $cur = Window ([string]$r.WorkingShift)
    $sh = $shiftByKey["$($r.EmployeeId)|$($d.ToString('yyyy-MM-dd'))"]
    $agentShift = $null
    if ($sh) { $agentShift = Window ("$($sh.ShiftStart)-$($sh.ShiftEnd)") }
    $correct = Intersect $agentShift $cw
    $human = ''
    if ($sh -and (-not [string]::IsNullOrWhiteSpace([string]$sh.ManualChangeBy))) { $human = 'S60028-or-manual' }
    $status = ''
    if (-not $cw) {
        $status = 'CENTRE_CLOSED_OR_NO_HOURS'
    } elseif (-not $cur) {
        $status = 'NO_USABLE_WORKINGSHIFT'
    } else {
        $outside = ((M $cur.Start) -lt (M $cw.Start)) -or ((M $cur.End) -gt (M $cw.End))
        if ($outside) { $status = 'OUTSIDE' } else { $status = 'INSIDE' }
    }
    $out.Add([pscustomobject]@{
        Id = $r.Id; EmpId = $r.EmployeeId; Date = $d.ToString('yyyy-MM-dd'); Centre = $r.SupportLocation
        Status = $status; Current = $r.WorkingShift
        CentreWindow = if ($cw) { "$($cw.Start)-$($cw.End)" } else { '' }
        AgentShift = if ($agentShift) { "$($agentShift.Start)-$($agentShift.End)" } else { '' }
        Correct = if ($correct) { "$($correct.Start)-$($correct.End)" } else { '' }
        HumanOwned = $human
    }) | Out-Null
}
if (-not (Test-Path -LiteralPath $ReportDir)) { New-Item -ItemType Directory -Path $ReportDir -Force | Out-Null }
$out | Export-Csv (Join-Path $ReportDir 'p5-analysis.csv') -NoTypeInformation -Encoding UTF8
Write-Output ("TOTAL IsOnSite rows >= {0}: {1}" -f $From, $out.Count)
$out | Group-Object Status | Select-Object Count, Name | Format-Table -AutoSize
Write-Output "--- OUTSIDE rows (candidate writes) ---"
$out | Where-Object Status -eq 'OUTSIDE' | Select-Object Id,EmpId,Date,Centre,Current,CentreWindow,AgentShift,Correct,HumanOwned | Format-Table -AutoSize
Write-Output "--- NOT INSIDE (non-OUTSIDE anomalies) ---"
$out | Where-Object { $_.Status -ne 'INSIDE' -and $_.Status -ne 'OUTSIDE' } | Select-Object Id,EmpId,Date,Centre,Status,Current,CentreWindow,AgentShift,Correct | Format-Table -AutoSize
$conn.Close()
