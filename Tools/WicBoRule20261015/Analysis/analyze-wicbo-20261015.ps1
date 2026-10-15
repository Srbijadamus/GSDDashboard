# WIC vs BO analysis 2026-10-15 -- READ-ONLY (v3).
#   * extract2.out is Windows-1252 (sqlcmd -o) -> read with codepage 1252.
#   * bo-rows-classified.csv was re-exported on 2026-10-15 21:00 (Tools/WicBoRule20261015/Export):
#     UTF-8 WITH BOM, real umlauts, and the 15th column is now correctly named MainCode.
#     The pre-fix file is kept as bo-rows-classified.pre-utf8fix-20261015.csv (its 15th column
#     was labelled HasRegional while holding the MAIN LocationCode -> read $_.HasRegional).
# No DB write, no deploy, no employee names in the output; RTM KID S60028 never used
# (login is the DEV operator S69307); /api/wic/open is a read-only GET.
$ErrorActionPreference = 'Stop'
$Inv = [System.Globalization.CultureInfo]::InvariantCulture
$Root = 'C:\GSDDashboard_secure'
$Work = Join-Path $Root '.tmp-wicbo-analysis-20261015'
$OutDir = Join-Path $Root 'Tools\WicBoRule20261015\Analysis'
$CsvPath = Join-Path $Root 'Tools\WicBoRule20261015\bo-rows-classified.csv'
$Base = 'http://127.0.0.1:5000'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Fold([string]$s) {
    if ($null -eq $s) { return '' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) { if ([int]$ch -lt 128) { [void]$sb.Append([char]::ToLowerInvariant($ch)) } }
    return $sb.ToString().Trim()
}

# ------------------------------------------------------------------ CSV
$all = @(Import-Csv -LiteralPath $CsvPath | ForEach-Object {
    $_ | Add-Member -NotePropertyName MainCode    -NotePropertyValue $_.MainCode -Force -PassThru |
         Add-Member -NotePropertyName ToolCentre  -NotePropertyValue $_.WicCentre   -Force -PassThru |
         Add-Member -NotePropertyName ToolShift   -NotePropertyValue $_.WorkingShift -Force -PassThru
})
$manual = @($all | Where-Object { $_.Classification -eq 'MANUAL_RTM_BO' })
$planNo = @($all | Where-Object { $_.Classification -eq 'PLAN_BO_NO_OPEN_CENTRE' })
"CSV total=$($all.Count)  MANUAL_RTM_BO=$($manual.Count)  PLAN_BO_NO_OPEN_CENTRE=$($planNo.Count)"
$all | Group-Object Classification | Sort-Object Name | ForEach-Object { "   $($_.Name) = $($_.Count)" }

# ------------------------------------------------------------------ DB dump (1252)
$raw = Get-Content -LiteralPath (Join-Path $Work 'extract2.out') -Encoding 1252
function Section([string]$name) {
    $s = ($raw | Select-String -Pattern "^=== $name ===$").LineNumber
    if (-not $s) { throw "section $name not found" }
    $rest = @($raw[$s..($raw.Count - 1)])
    $next = $rest | Select-String -Pattern '^=== ' | Select-Object -First 1
    $end = if ($next) { $s + $next.LineNumber - 2 } else { $raw.Count - 1 }
    return @($raw[$s..$end] | Where-Object { $_ -match '\S' })
}
$assignRows = Section 'ASSIGN'; $locRows = Section 'LOC'; $hoursRows = Section 'HOURS'; $holRows = Section 'HOL'

$loc = @{}
foreach ($l in $locRows) {
    $p = @($l.Split('|') | ForEach-Object { $_.Trim() }); if ($p.Count -lt 9) { continue }
    $loc[$p[1]] = [pscustomobject]@{ Code=$p[1]; Name=$p[2]; Legacy=$p[3]; Postal=$p[4]; Country=$p[5]; Bundesland=$p[6]; Active=($p[7] -eq '1'); MinReq=[int]$p[8] }
}
$legacyToCode = @{}
foreach ($k in $loc.Keys) { if ($loc[$k].Legacy) { $legacyToCode[$loc[$k].Legacy] = $k } }
$nameByFold = @{}
foreach ($k in $loc.Keys) { $nameByFold[(Fold $loc[$k].Name)] = $k }
function Get-LocName([string]$code) { if ($code -and $loc.ContainsKey($code) -and $loc[$code].Name) { return $loc[$code].Name } return "$code" }

$tiers = @('MAIN','BACKUP','REGIONAL')
$byEmp = @{}
foreach ($a in $assignRows) {
    $p = @($a.Split('|') | ForEach-Object { $_.Trim() }); if ($p.Count -lt 5) { continue }
    $code = $p[4]
    if (-not $code) { if ($legacyToCode.ContainsKey($p[3])) { $code = $legacyToCode[$p[3]] } else { $code = $p[3] } }
    $t = $p[1].ToUpperInvariant(); $rank = $tiers.IndexOf($t); if ($rank -lt 0) { $rank = 99 }
    if (-not $byEmp.ContainsKey($p[0])) { $byEmp[$p[0]] = New-Object System.Collections.ArrayList }
    [void]$byEmp[$p[0]].Add([pscustomobject]@{ Tier=$t; Rank=$rank; Id=[int]$p[2]; Code=$code })
}
$index = @{}
foreach ($emp in $byEmp.Keys) {
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $list = New-Object System.Collections.ArrayList
    foreach ($e in ($byEmp[$emp] | Sort-Object Rank, Id)) { if ($seen.Add($e.Code)) { [void]$list.Add($e) } }
    $index[$emp] = $list
}
"employees with an active MAIN/BACKUP/REGIONAL assignment: $($index.Count)"

$hoursByCode = @{}
foreach ($h in $hoursRows) {
    $p = @($h.Split('|') | ForEach-Object { $_.Trim() }); if ($p.Count -lt 12) { continue }
    if (-not $hoursByCode.ContainsKey($p[1])) { $hoursByCode[$p[1]] = New-Object System.Collections.ArrayList }
    [void]$hoursByCode[$p[1]].Add([pscustomobject]@{
        Code=$p[1]; Dow=[int]$p[2]; Open=$p[3]; Close=$p[4]; Open2=$p[5]; Close2=$p[6]; IsClosed=($p[7] -eq '1')
        EffFrom=$(if ($p[8]) { [datetime]::ParseExact($p[8],'yyyy-MM-dd',$Inv) } else { $null })
        ExDate=$(if ($p[9]) { [datetime]::ParseExact($p[9],'yyyy-MM-dd',$Inv) } else { $null })
        MinReq=[int]$p[10]; Note=$p[11] })
}
$holNat = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$holByBl = @{}
foreach ($h in $holRows) {
    $p = @($h.Split('|') | ForEach-Object { $_.Trim() }); if ($p.Count -lt 3) { continue }
    if ($p[1] -eq '1') { [void]$holNat.Add($p[0]) }
    if ($p[2]) { if (-not $holByBl.ContainsKey($p[2])) { $holByBl[$p[2]] = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase) }; [void]$holByBl[$p[2]].Add($p[0]) }
}
"location rows=$($loc.Count)  opening-hours rows=$($hoursRows.Count)  holiday rows=$($holRows.Count)"

# ---------------------------------------------------------------- DB provenance for the 15 manual rows
$m15 = @{}
$secM15 = Section 'MANUAL15'
foreach ($l in $secM15) {
    $p = @($l.Split('|') | ForEach-Object { $_.Trim() }); if ($p.Count -lt 15) { continue }
    $m15[$p[0]] = [pscustomobject]@{
        Id=$p[0]; EmployeeId=$p[1]; Date=$p[2]; ShiftType=$p[3]; RawValue=$p[4]; AgentTask=$p[5]
        Start=$p[6]; End=$p[7]; IsWicDuty=$p[8]; AutoGenerated=$p[9]; SourceModule=$p[10]; SourceId=$p[11]
        ManualChangeBy=$p[12]; ManualChangeAt=$p[13]; Note=$p[14]
    }
}
$batches = @{}
foreach ($l in (Section 'BATCHHDR')) {
    $p = @($l.Split('|') | ForEach-Object { $_.Trim() }); if ($p.Count -lt 13) { continue }
    $batches[$p[0]] = [pscustomobject]@{
        Id=$p[0]; EmployeeId=$p[1]; DateFrom=$p[2]; DateTo=$p[3]; ShiftStart=$p[4]; ShiftEnd=$p[5]
        WorkingDays=$p[6]; AgentTask=$p[7]; LocationCode=$p[8]; RowCount=$p[9]; ReplacedExisting=$p[10]
        CreatedByKid=$p[11]; CreatedAt=$p[12]
    }
}
$preimg = @()
foreach ($l in (Section 'PREIMG')) {
    $p = @($l.Split('|') | ForEach-Object { $_.Trim() }); if ($p.Count -lt 13) { continue }
    $preimg += [pscustomobject]@{
        Batch=$p[0]; EmployeeId=$p[1]; Date=$p[2]; ShiftType=$p[3]; RawValue=$p[4]; Start=$p[5]; End=$p[6]
        IsWicDuty=$p[7]; AgentTask=$p[8]; AutoGenerated=$p[9]; SourceModule=$p[10]; SourceId=$p[11]; WicEntries=$p[12]
    }
}
"manual15 rows=$($m15.Count)  batches=$($batches.Count)  pre-image rows=$($preimg.Count)"

function Get-PreImageText([string]$id, [string]$emp, [string]$date, [string]$sourceId) {
    $b = if ($sourceId -and $batches.ContainsKey($sourceId)) { $batches[$sourceId] } else { $null }
    $head = if ($b) {
        "batch $($b.Id) ($($b.CreatedByKid), created $($b.CreatedAt), range $($b.DateFrom)..$($b.DateTo), $($b.ShiftStart)-$($b.ShiftEnd), days $($b.WorkingDays), centre $(Get-LocName $b.LocationCode), rows $($b.RowCount), replaced $($b.ReplacedExisting))"
    } else { "batch (SourceId=$sourceId): no RosterBatches row" }
    $pi = @($preimg | Where-Object { $_.Batch -eq $sourceId -and $_.EmployeeId -eq $emp -and $_.Date -eq $date })
    if ($pi.Count -eq 0) {
        return "$head -- NO pre-image stored for this day: the batch created the row itself ($($b.RowCount) row(s), ReplacedExisting=$($b.ReplacedExisting))"
    }
    $x = $pi[0]
    $raw = if ($x.RawValue) { $x.RawValue } else { '(none)' }
    return "$head -- pre-image: $($x.ShiftType) / RawValue=$raw / $($x.Start)-$($x.End) / IsWicDuty=$($x.IsWicDuty) / AgentTask=$(if ($x.AgentTask) { $x.AgentTask } else { 'NULL' }) / AutoGenerated=$($x.AutoGenerated) / SourceModule=$(if ($x.SourceModule) { $x.SourceModule } else { '(none)' }) / SourceId=$(if ($x.SourceId) { $x.SourceId } else { '(none)' }) / WicEntries=$($x.WicEntries)"
}

# ------------------------------------------------------------------ WicHoursResolver / WicWorkingShift ports
function Get-ResolvedHours([string]$code, [datetime]$date) {
    if (-not $hoursByCode.ContainsKey($code)) { return $null }
    $allH = $hoursByCode[$code]
    $ex = $allH | Where-Object { $_.ExDate -eq $date } | Sort-Object EffFrom -Descending | Select-Object -First 1
    if ($ex) { return $ex }
    $dow = [int]$date.DayOfWeek
    return ($allH | Where-Object { $null -eq $_.ExDate -and $_.Dow -eq $dow -and ($null -eq $_.EffFrom -or $_.EffFrom -le $date) } | Sort-Object EffFrom -Descending | Select-Object -First 1)
}
function Get-Intervals($h) {
    $list = New-Object System.Collections.ArrayList
    if ($null -eq $h -or $h.IsClosed) { return $list }
    $s1 = [TimeOnly]::MinValue; $e1 = [TimeOnly]::MinValue; $s2 = [TimeOnly]::MinValue; $e2 = [TimeOnly]::MinValue
    # NOTE: elements are objects, not @(start,end) pairs -- PowerShell unrolls nested arrays
    # on function return, which silently split every pair into two scalars.
    if ([TimeOnly]::TryParse($h.Open,[ref]$s1)  -and [TimeOnly]::TryParse($h.Close,[ref]$e1))  { [void]$list.Add([pscustomobject]@{ Start=$s1; End=$e1 }) }
    if ([TimeOnly]::TryParse($h.Open2,[ref]$s2) -and [TimeOnly]::TryParse($h.Close2,[ref]$e2)) { [void]$list.Add([pscustomobject]@{ Start=$s2; End=$e2 }) }
    return $list
}
function Get-IsOpen([string]$code, [datetime]$date) { $h = Get-ResolvedHours $code $date; return ($null -ne $h -and -not $h.IsClosed -and @(Get-Intervals $h).Count -gt 0) }
function FmtT($t) { return ('{0:D2}:{1:D2}' -f $t.Hour, $t.Minute) }
function FmtSpan($a, $b) { return ((FmtT $a) + '-' + (FmtT $b)) }
function Get-WindowStr([string]$code, [datetime]$date) {
    $parts = @()
    foreach ($iv in @(Get-Intervals (Get-ResolvedHours $code $date))) { $parts += (FmtSpan $iv.Start $iv.End) }
    return ($parts -join '+')
}
function Get-OpenPairs([string]$emp, [datetime]$date) { if (-not $index.ContainsKey($emp)) { return @() }; return @($index[$emp] | Where-Object { Get-IsOpen $_.Code $date }) }
function Get-PriorityCentre([string]$emp, [datetime]$date) {
    if (-not $index.ContainsKey($emp)) { return $null }
    foreach ($t in $tiers) {
        $open = @($index[$emp] | Where-Object { $_.Tier -eq $t } | Where-Object { Get-IsOpen $_.Code $date })
        if ($open.Count -gt 1) { return $null }
        if ($open.Count -eq 1) { return $open[0].Code }
    }
    return $null
}
function Get-PriorityTier([string]$emp, [datetime]$date) {
    if (-not $index.ContainsKey($emp)) { return '' }
    foreach ($t in $tiers) { if (@($index[$emp] | Where-Object { $_.Tier -eq $t } | Where-Object { Get-IsOpen $_.Code $date }).Count -gt 0) { return $t } }
    return ''
}
function Get-FirstTier([string]$emp) { foreach ($t in $tiers) { if (@($index[$emp] | Where-Object { $_.Tier -eq $t }).Count -gt 0) { return $t } }; return '' }
function Get-DutyWindow([string]$code, [datetime]$date, [string]$shiftStart, [string]$shiftEnd) {
    $h = Get-ResolvedHours $code $date; if ($null -eq $h -or $h.IsClosed) { return $null }
    $s1 = [TimeOnly]::MinValue; $e1 = [TimeOnly]::MinValue; $s2 = [TimeOnly]::MinValue; $e2 = [TimeOnly]::MinValue
    $has1 = [TimeOnly]::TryParse($h.Open,[ref]$s1)  -and [TimeOnly]::TryParse($h.Close,[ref]$e1)
    $has2 = [TimeOnly]::TryParse($h.Open2,[ref]$s2) -and [TimeOnly]::TryParse($h.Close2,[ref]$e2)
    if (-not $has1 -and -not $has2) { return $null }
    $cs = if ($has1) { $s1 } else { $s2 }; $ce = if ($has2) { $e2 } else { $e1 }
    $s = [TimeOnly]::MinValue; $e = [TimeOnly]::MinValue
    if (-not [TimeOnly]::TryParse($shiftStart,[ref]$s) -or -not [TimeOnly]::TryParse($shiftEnd,[ref]$e)) { return $null }
    $st = if ($cs -gt $s) { $cs } else { $s }; $en = if ($ce -lt $e) { $ce } else { $e }
    if ($en -le $st) { return $null }
    return @($st,$en)
}

# ------------------------------------------------------------------ /api/wic/open
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$login = Invoke-RestMethod -Uri "$Base/api/auth/login" -Method Post -ContentType 'application/json' `
    -Body (@{ kid='S69307' } | ConvertTo-Json) -WebSession $session -Headers $H -TimeoutSec 30
"login KID=S69307 role=$($login.role)  -- RTM KID S60028 not used"

$dates = @($all | ForEach-Object { $_.ShiftDate } | Sort-Object -Unique)
# the API is read for EVERY calendar day of the window, so task 3 is not biased by the
# days that happen to carry a BO row.
$apiDates = @()
for ($dt = [datetime]'2026-10-15'; $dt -le [datetime]'2027-02-28'; $dt = $dt.AddDays(1)) { $apiDates += $dt.ToString('yyyy-MM-dd') }
$apiByDate = @{}; $apiErrors = @()
$t0 = Get-Date
foreach ($d in $apiDates) {
    try { $apiByDate[$d] = Invoke-RestMethod -Uri "$Base/api/wic/open?date=$d&horizon=1" -WebSession $session -Headers $H -TimeoutSec 120 }
    catch { $apiErrors += "$d : $($_.Exception.Message)" }
}
"api /api/wic/open days=$($apiByDate.Count) errors=$($apiErrors.Count) in $([int]((Get-Date)-$t0).TotalSeconds)s"
function Get-ApiCentre([string]$code, [string]$date) {
    if (-not $apiByDate.ContainsKey($date)) { return $null }
    $day = @($apiByDate[$date]); if ($day.Count -eq 0) { return $null }
    return (@($day[0].locations) | Where-Object { $_.locationCode -eq $code } | Select-Object -First 1)
}
# /api/wic/cards carries the agent IDENTITIES of an on-site WIC row (the /api/wic/open DTO
# has counts only). Used for "is this centre covered by ANOTHER agent that day".
$cardsByDate = @{}; $cardErrors = @()
$tc = Get-Date
foreach ($d in $apiDates) {
    try { $cardsByDate[$d] = Invoke-RestMethod -Uri "$Base/api/wic/cards?date=$d" -WebSession $session -Headers $H -TimeoutSec 180 }
    catch { $cardErrors += "$d : $($_.Exception.Message)" }
}
"api /api/wic/cards days=$($cardsByDate.Count) errors=$($cardErrors.Count) in $([int]((Get-Date)-$tc).TotalSeconds)s"
function Get-CardAgents([string]$code, [string]$date) {
    if (-not $cardsByDate.ContainsKey($date)) { return @() }
    $card = @($cardsByDate[$date]) | Where-Object { $_.locationCode -eq $code } | Select-Object -First 1
    if (-not $card) { return @() }
    return @($card.assignedAgents | ForEach-Object { $_.employeeId })
}

# ------------------------------------------------------------------ evaluate every CSV row
$evalAll = @()
foreach ($r in $all) {
    $date = [datetime]::ParseExact($r.ShiftDate,'yyyy-MM-dd',$Inv)
    $open = Get-OpenPairs $r.EmployeeId $date
    $eff  = Get-PriorityCentre $r.EmployeeId $date
    $ft   = Get-FirstTier $r.EmployeeId
    $ftOpen = @($open | Where-Object { $_.Tier -eq $ft })
    # the first tier that actually HAS an open centre (what PriorityCentre examines first)
    $fot = ''; $fotCount = 0
    foreach ($t in $tiers) {
        $c = @($open | Where-Object { $_.Tier -eq $t })
        if ($c.Count -gt 0) { $fot = $t; $fotCount = $c.Count; break }
    }
    $evalAll += [pscustomobject]@{ Row=$r; Date=$date; Open=$open; Effective=$eff; PriorityTier=(Get-PriorityTier $r.EmployeeId $date); FirstTier=$ft; FirstTierOpenCount=$ftOpen.Count; FirstOpenTier=$fot; FirstOpenTierCount=$fotCount }
}
"`n--- validation: HoursResolver port vs the 2026-10-15 tool's Classification (all 926 rows) ---"
$matrix = @{}
foreach ($e in $evalAll) {
    $my = if ($e.Open.Count -eq 0) { 'a_no_open' } elseif ($e.Open.Count -ge 2) { 'b_2plus_open' } else { 'c_one_open' }
    $k = "$($e.Row.Classification)|$my"; if (-not $matrix.ContainsKey($k)) { $matrix[$k] = 0 }; $matrix[$k]++
}
$matrix.Keys | Sort-Object | ForEach-Object { "   $($_) = $($matrix[$_])" }

# ------------------------------------------------------------------ TASK 1
$t1 = @()
foreach ($r in $manual) {
    $date = [datetime]::ParseExact($r.ShiftDate,'yyyy-MM-dd',$Inv)
    $emp = $r.EmployeeId
    $open = Get-OpenPairs $emp $date
    $eff  = Get-PriorityCentre $emp $date
    $assignedNames = if ($index.ContainsKey($emp)) { @($index[$emp] | ForEach-Object { Get-LocName $_.Code }) } else { @() }
    $at = $r.AgentTask; $atFold = Fold $at
    $match = 'NO (AgentTask empty)'
    if ($atFold) {
        if (@($assignedNames | Where-Object { (Fold $_) -eq $atFold }).Count -gt 0) { $match = 'YES (exact, ASCII-folded)' }
        elseif (@($assignedNames | Where-Object { $n = Fold $_; $n.StartsWith($atFold) -or $atFold.StartsWith($n) }).Count -gt 0) { $match = 'YES (prefix/truncated, ASCII-folded)' }
        else { $match = 'NO' }
    }
    $target = if ($eff) { $eff } elseif ($r.MainCode) { $r.MainCode } else { '' }
    $api = if ($target) { Get-ApiCentre $target $r.ShiftDate } else { $null }
    $db = if ($m15.ContainsKey($r.Id)) { $m15[$r.Id] } else { $null }
    $t1 += [pscustomobject]@{
        Id=$r.Id; EmployeeId=$emp; Date=$r.ShiftDate; Day=$r.Dow
        ShiftType=$(if ($db) { $db.ShiftType } else { $r.ShiftType }); RawValue=$(if ($db) { $db.RawValue } else { $r.RawValue }); Shift="$($r.Start)-$($r.End)"
        AgentTask=$at; AgentTaskIsCentreName=$match
        CsvMainCode=$r.MainCode; CsvMainName=$(Get-LocName $r.MainCode)
        OpenAssignedCentres=(@($open | ForEach-Object { (Get-LocName $_.Code) + '(' + $_.Tier + ')' }) -join ' + ')
        PriorityCentreNow=$(if ($eff) { Get-LocName $eff } else { '(none)' })
        CheckedCentre=$(if ($target) { Get-LocName $target } else { '' })
        ApiStatus=$(if ($api) { $api.coverageStatus } else { 'n/a' })
        ApiOpen=$(if ($api) { $api.isOpen } else { '' })
        ApiScheduled=$(if ($api) { $api.scheduledCount } else { '' })
        ApiEffective=$(if ($api) { $api.effectiveCoverage } else { '' })
        ApiMinRequired=$(if ($api) { $api.minRequired } else { '' })
        HasMain=$r.HasMain; HasBackup=$r.HasBackup
        DbIsWicDuty=$(if ($db) { $db.IsWicDuty } else { '' })
        DbAutoGenerated=$(if ($db) { $db.AutoGenerated } else { '' })
        DbSourceModule=$(if ($db) { $db.SourceModule } else { '' })
        DbSourceId=$(if ($db) { $db.SourceId } else { '' })
        ManualChangeBy=$(if ($db) { $db.ManualChangeBy } else { $r.ManualChangeBy })
        ManualChangeAt=$(if ($db) { $db.ManualChangeAt } else { $r.ManualChangeAt })
        ManualChangeNote=$(if ($db) { $db.Note } else { '' })
        PreImage=($(if ($db) { Get-PreImageText $r.Id $emp $r.ShiftDate $db.SourceId } else { '' }))
    }
}
"TASK1 rows=$($t1.Count)  AgentTask is one of the employee's own centre names: $(@($t1 | Where-Object { $_.AgentTaskIsCentreName -like 'YES*' }).Count)"

# ------------------------------------------------------------------ TASK 2 split
$A = @($evalAll | Where-Object { $_.Row.Classification -eq 'PLAN_BO_NO_OPEN_CENTRE' -and $_.Open.Count -eq 0 })
$B = @($evalAll | Where-Object { $_.Row.Classification -eq 'PLAN_BO_NO_OPEN_CENTRE' -and $_.Open.Count -ge 2 })
$C = @($evalAll | Where-Object { $_.Row.Classification -eq 'PLAN_BO_NO_OPEN_CENTRE' -and $_.Open.Count -eq 1 })
"(a) no assigned centre open on the day = $($A.Count)"
"(b) 2 or more assigned centres open    = $($B.Count)"
"(c) exactly one assigned centre open   = $($C.Count)   <- the tool also filed these under NO_OPEN_CENTRE"
$allB = @($evalAll | Where-Object { $_.Open.Count -ge 2 })
"2+ open assigned centres anywhere in the 926: $($allB.Count)"
$allB | Group-Object { $_.Row.Classification } | ForEach-Object { "   $($_.Name) = $($_.Count)" }

# ------------------------------------------------------------------ TASK 2b
$t2b = @()
foreach ($rec in $B) {
    $r = $rec.Row; $date = $rec.Date; $emp = $r.EmployeeId
    $centreDetail = @(); $names = @(); $windows = @(); $duty = @()
    foreach ($p in $rec.Open) {
        $names += Get-LocName $p.Code
        $w = Get-WindowStr $p.Code $date; $windows += $w
        $dw = Get-DutyWindow $p.Code $date $r.Start $r.End
        if ($dw) { $dws = FmtSpan $dw[0] $dw[1] } else { $dws = 'no-overlap' }
        if ($dw) { $duty += ,@($p.Code, $dw[0], $dw[1]) }
        $api = Get-ApiCentre $p.Code $r.ShiftDate
        $allIds = @(Get-CardAgents $p.Code $r.ShiftDate | Sort-Object -Unique)
        $otherIds = @($allIds | Where-Object { $_ -ne $emp })
        $centreDetail += [pscustomobject]@{
            Code=$p.Code; Name=(Get-LocName $p.Code); Tier=$p.Tier; Window=$w; DutySlice=$dws
            ApiStatus=$(if ($api) { $api.coverageStatus } else { 'n/a' }); ApiOpen=$(if ($api) { $api.isOpen } else { '' })
            Scheduled=$(if ($api) { $api.scheduledCount } else { '' }); Effective=$(if ($api) { $api.effectiveCoverage } else { '' })
            MinReq=$(if ($api) { $api.minRequired } else { '' }); AgentsAll=($allIds -join ','); AgentsOther=($otherIds -join ',')
            CoveredByAnother=($otherIds.Count -gt 0) }
    }
    $overlap = $false; $overlapPairs = 0
    for ($i = 0; $i -lt $rec.Open.Count; $i++) {
        for ($j = $i + 1; $j -lt $rec.Open.Count; $j++) {
            $hit = $false
            foreach ($x in @(Get-Intervals (Get-ResolvedHours $rec.Open[$i].Code $date))) {
                foreach ($y in @(Get-Intervals (Get-ResolvedHours $rec.Open[$j].Code $date))) {
                    if ($x.Start -lt $y.End -and $y.Start -lt $x.End) { $hit = $true } } }
            if ($hit) { $overlap = $true; $overlapPairs++ }
        }
    }
    $span = $null
    if ($duty.Count -ge 2) {
        $st = ($duty | ForEach-Object { $_[1] } | Sort-Object)[0]
        $en = ($duty | ForEach-Object { $_[2] } | Sort-Object)[-1]
        if ($en -gt $st) { $span = FmtSpan $st $en }
    }
    $shiftStart = [TimeOnly]::MinValue; $shiftEnd = [TimeOnly]::MinValue
    $shiftOk = [TimeOnly]::TryParse($r.Start,[ref]$shiftStart) -and [TimeOnly]::TryParse($r.End,[ref]$shiftEnd)
    $spanContained = $false
    if ($span -and $shiftOk) {
        $sp = $span.Split('-'); $ss = [TimeOnly]::Parse($sp[0]); $se = [TimeOnly]::Parse($sp[1])
        $spanContained = ($shiftStart -le $ss -and $shiftEnd -ge $se)
    }
    $ambiguous = ($null -eq $rec.Effective -and $rec.FirstOpenTierCount -gt 1)
    $t2b += [pscustomobject]@{
        EmployeeId=$emp; Date=$r.ShiftDate; Day=$r.Dow; Shift="$($r.Start)-$($r.End)"
        OpenCentreCount=$rec.Open.Count
        SubCase=$(if ($ambiguous) { 'AMBIGUOUS_FIRST_OPEN_TIER' } else { 'ONE_OPEN_PER_TIER' })
        FirstTier=$rec.FirstTier; FirstOpenTier=$rec.FirstOpenTier; FirstOpenTierCount=$rec.FirstOpenTierCount
        PriorityTier=$rec.PriorityTier
        PriorityCentre=$(if ($rec.Effective) { Get-LocName $rec.Effective } else { '' })
        OpenCentres=($names -join ' + '); CentreWindows=($windows -join ' | ')
        DutySlices=(@($centreDetail | ForEach-Object { "$($_.Name):$($_.DutySlice)" }) -join ' | ')
        WindowsOverlap=$overlap; OverlappingCentrePairs=$overlapPairs
        MultiCentreSpan=$span; SpanContainedInShift=$spanContained
        AllCentreWindowsDisjoint=(-not $overlap)
        CoveredByAnother=(@($centreDetail | Where-Object { $_.CoveredByAnother } | ForEach-Object { $_.Name }) -join ',')
        NotCoveredByAnother=(@($centreDetail | Where-Object { -not $_.CoveredByAnother } | ForEach-Object { $_.Name }) -join ',')
        ApiUncoveredCentres=(@($centreDetail | Where-Object { $_.ApiStatus -eq 'UNCOVERED' } | ForEach-Object { $_.Name }) -join ',')
        ApiDetail=($centreDetail | ForEach-Object { "$($_.Name)[$($_.Tier)] win=$($_.Window) duty=$($_.DutySlice) open=$($_.ApiOpen) status=$($_.ApiStatus) eff=$($_.Effective)/$($_.MinReq) sched=$($_.Scheduled) agents=[$($_.AgentsAll)]" }) -join ' ;; '
    }
}

# ------------------------------------------------------------------ TASK 3
$set1Emps = @($manual | ForEach-Object { $_.EmployeeId } | Sort-Object -Unique)
$set2bEmps = @($t2b | ForEach-Object { $_.EmployeeId } | Sort-Object -Unique)
$poolStrict = @($set1Emps + $set2bEmps | Sort-Object -Unique)
$poolAll = @($all | ForEach-Object { $_.EmployeeId } | Sort-Object -Unique)
$boOnDate = @{}
foreach ($r in $all) { $boOnDate["$($r.EmployeeId)|$($r.ShiftDate)"] = $r.Classification }
"task3 agent pools: set1(manual)=$($set1Emps.Count) employees, set2b=$($set2bEmps.Count), all BO-row agents=$($poolAll.Count)"

$t3 = @(); $t3all = @()
foreach ($d in ($apiDates | Sort-Object)) {
    if (-not $apiByDate.ContainsKey($d)) { continue }
    $date = [datetime]::ParseExact($d,'yyyy-MM-dd',$Inv)
    $day = @($apiByDate[$d]); if ($day.Count -eq 0) { continue }
    foreach ($l in @($day[0].locations)) {
        if ($l.coverageStatus -ne 'UNCOVERED') { continue }
        $code = $l.locationCode
        $hStrict = @(); $hAll = @()
        foreach ($emp in $poolAll) {
            if (-not $index.ContainsKey($emp)) { continue }
            if (@($index[$emp] | Where-Object { $_.Code -eq $code }).Count -eq 0) { continue }
            $boCls = if ($boOnDate.ContainsKey("$emp|$d")) { $boOnDate["$emp|$d"] } else { 'no-BO-row-that-day' }
            $tiersOf = (@($index[$emp] | Where-Object { $_.Code -eq $code } | ForEach-Object { $_.Tier }) -join '/')
            $entry = [pscustomobject]@{ EmployeeId=$emp; AssignmentTier=$tiersOf; BoOnThatDay=$boCls }
            $hAll += $entry
            if ($poolStrict -contains $emp) { $hStrict += $entry }
        }
        if ($hAll.Count -eq 0) { continue }
        $row = [pscustomobject]@{
            Date=$d; Day=$date.DayOfWeek.ToString(); CentreCode=$code; CentreName=$l.displayName
            CentreOpenHours=(Get-WindowStr $code $date); MinRequired=$l.minRequired
            Scheduled=$l.scheduledCount; Effective=$l.effectiveCoverage
            OnSiteAgentsFromCards=(@(Get-CardAgents $code $d | Sort-Object -Unique) -join ',')
            BoAgentCountStrict=$hStrict.Count
            BoAgentsStrict=(@($hStrict | ForEach-Object { "$($_.EmployeeId)[$($_.AssignmentTier);BO=$($_.BoOnThatDay)]" }) -join ' ')
            BoAgentCountAll=$hAll.Count
            BoAgentsAll=(@($hAll | ForEach-Object { "$($_.EmployeeId)[$($_.AssignmentTier);BO=$($_.BoOnThatDay)]" }) -join ' ')
        }
        $t3 += $row
        $t3all += $row
    }
}
$t3strict = @($t3 | Where-Object { $_.BoAgentCountStrict -gt 0 })
"TASK3 rows: strict pool=$($t3strict.Count)  any BO-row agent=$($t3all.Count)"

# ------------------------------------------------------------------ report aggregates
"`n--- (a)/(b) per employee ---"
$aggA = @($A | Group-Object { $_.Row.EmployeeId } | Sort-Object { -$_.Count } | ForEach-Object { [pscustomobject]@{ EmployeeId=$_.Name; A_noOpen=$_.Count } })
$aggB = @($B | Group-Object { $_.Row.EmployeeId } | Sort-Object { -$_.Count } | ForEach-Object { [pscustomobject]@{ EmployeeId=$_.Name; B_twoPlusOpen=$_.Count } })
foreach ($e in @($aggA.EmployeeId + $aggB.EmployeeId | Sort-Object -Unique)) {
    $x = @($aggA | Where-Object EmployeeId -eq $e)
    $y = @($aggB | Where-Object EmployeeId -eq $e)
    "   $e  a=$($(if($x){$x[0].A_noOpen}else{0}))  b=$($(if($y){$y[0].B_twoPlusOpen}else{0}))"
}
"--- (b) detail ---"
"   rows=$($t2b.Count)  employees=$(@($t2b.EmployeeId | Sort-Object -Unique) -join ',')"
$t2b | Group-Object SubCase | ForEach-Object { "   SubCase $($_.Name) = $($_.Count)" }
$t2b | Group-Object WindowsOverlap | ForEach-Object { "   WindowsOverlap=$($_.Name) = $($_.Count)" }
$t2b | Group-Object AllCentreWindowsDisjoint | ForEach-Object { "   AllCentreWindowsDisjoint=$($_.Name) = $($_.Count)" }
$t2b | Group-Object SpanContainedInShift | ForEach-Object { "   SpanContainedInShift=$($_.Name) = $($_.Count)" }
$t2b | Group-Object OpenCentreCount | Sort-Object Name | ForEach-Object { "   OpenCentreCount=$($_.Name) = $($_.Count)" }
$bCentres = @($t2b | ForEach-Object { $_.OpenCentres -split ' \+ ' } | Sort-Object -Unique)
"   distinct open centres involved in (b) = $($bCentres.Count): $($bCentres -join '; ')"
"   (b) rows whose open centres are NOT all covered by another agent: $(@($t2b | Where-Object { $_.NotCoveredByAnother -ne '' }).Count)"

# ------------------------------------------------------------------ CSV writes
function Write-Csv([string]$name, $rows) {
    $p = Join-Path $OutDir $name
    if (@($rows).Count -eq 0) { Set-Content -LiteralPath $p -Value '' -Encoding UTF8 } else { @($rows) | Export-Csv -LiteralPath $p -NoTypeInformation -Encoding UTF8 }
    return $p
}
$t1Rows = @($t1 | Select-Object Id,EmployeeId,Date,Day,ShiftType,RawValue,Shift,AgentTask,AgentTaskIsCentreName,CsvMainCode,CsvMainName,OpenAssignedCentres,PriorityCentreNow,CheckedCentre,ApiStatus,ApiOpen,ApiScheduled,ApiEffective,ApiMinRequired,HasMain,HasBackup,DbIsWicDuty,DbAutoGenerated,DbSourceModule,DbSourceId,ManualChangeBy,ManualChangeAt,ManualChangeNote,PreImage)
$aRows  = @($A | ForEach-Object { [pscustomobject]@{ Id=$_.Row.Id; EmployeeId=$_.Row.EmployeeId; Date=$_.Row.ShiftDate; Day=$_.Row.Dow; Shift="$($_.Row.Start)-$($_.Row.End)"; MainCode=$_.Row.MainCode } })
$cRows  = @($C | ForEach-Object { [pscustomobject]@{ Id=$_.Row.Id; EmployeeId=$_.Row.EmployeeId; Date=$_.Row.ShiftDate; Day=$_.Row.Dow; Shift="$($_.Row.Start)-$($_.Row.End)"; OpenCentres=(@($_.Open | ForEach-Object { (Get-LocName $_.Code) + '(' + $_.Tier + ')' }) -join ' + '); PriorityCentre=$(if ($_.Effective) { Get-LocName $_.Effective } else { '' }) } })
$evalRows = @($evalAll | ForEach-Object {
    [pscustomobject]@{
        Id=$_.Row.Id; EmployeeId=$_.Row.EmployeeId; Date=$_.Row.ShiftDate; Day=$_.Row.Dow; Shift="$($_.Row.Start)-$($_.Row.End)"
        Classification=$_.Row.Classification
        AssignedCentres=(@($index[$_.Row.EmployeeId] | ForEach-Object { (Get-LocName $_.Code) + '(' + $_.Tier + ')' }) -join ', ')
        OpenCentres=(@($_.Open | ForEach-Object { (Get-LocName $_.Code) + '(' + $_.Tier + ')' }) -join ', ')
        OpenCount=$_.Open.Count; FirstTier=$_.FirstTier; FirstTierOpenCount=$_.FirstTierOpenCount
        MyCategory=$(if ($_.Open.Count -eq 0) { 'a_no_open_centre' } elseif ($_.Open.Count -ge 2) { 'b_2plus_open' } else { 'c_exactly_one_open' })
        PriorityCentre=$(if ($_.Effective) { Get-LocName $_.Effective } else { '' })
    }
})
$p1 = Write-Csv 'task1-manual-rtm-bo.csv' $t1Rows
$pa = Write-Csv 'task2a-no-open-centre.csv' $aRows
$pb = Write-Csv 'task2b-multi-open-centre.csv' $t2b
$pc = Write-Csv 'task2c-exactly-one-open-centre.csv' $cRows
$pe = Write-Csv 'task2-plan-bo-evaluation-all-926.csv' $evalRows
$p3 = Write-Csv 'task3-uncovered-centre-with-bo-agent.csv' $t3all

# ------------------------------------------------------------------ console + summary
"`n=== TASK 1: MANUAL_RTM_BO (15) ==="
$t1Rows | Format-Table Id,EmployeeId,Date,Day,Shift,AgentTask,AgentTaskIsCentreName,CsvMainName,PriorityCentreNow,CheckedCentre,ApiStatus,ApiOpen,ApiScheduled -AutoSize | Out-String -Width 320 | Write-Output
"`n=== TASK 2b: 2+ open assigned centres ==="
$t2b | Format-Table EmployeeId,Date,Day,Shift,OpenCentreCount,SubCase,OpenCentres,WindowsOverlap,OverlappingCentrePairs,MultiCentreSpan,SpanContainedInShift,CoveredByAnother,NotCoveredByAnother -AutoSize | Out-String -Width 400 | Write-Output
"`n=== TASK 3: UNCOVERED centre with a BO agent holding an active assignment ==="
$t3all | Format-Table Date,Day,CentreName,CentreOpenHours,MinRequired,Scheduled,Effective,BoAgentCountStrict,BoAgentsStrict,BoAgentCountAll -AutoSize | Out-String -Width 400 | Write-Output

$summary = [pscustomobject]@{
    generatedAt = (Get-Date).ToString('o')
    source = @{ csv=$CsvPath; db='localhost\SQLEXPRESS GSDDashboard (SELECT only)'; api="$Base/api/wic/open"; loginKid='S69307'; rtmKidUsed=$false }
    counts = [pscustomobject]@{
        csvTotal=$all.Count; manualRtmBo=$manual.Count; planBoNoOpenCentre=$planNo.Count
        a_noOpenCentre=$A.Count; b_twoPlusOpen=$B.Count; c_exactlyOneOpen=$C.Count
        task1Rows=$t1.Count; task1AgentTaskIsCentre=@($t1 | Where-Object { $_.AgentTaskIsCentreName -like 'YES*' }).Count
        task2bRows=$t2b.Count; task3StrictRows=$t3strict.Count; task3AllRows=$t3all.Count
    }
    task1=$t1Rows; task2b=$t2b; task3=$t3all
    files=@{ task1=$p1; task2a=$pa; task2b=$pb; task2c=$pc; task2all=$pe; task3=$p3 }
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'summary.json') -Encoding UTF8
"`nfiles:`n $p1`n $pa`n $pb`n $pc`n $pe`n $p3"
