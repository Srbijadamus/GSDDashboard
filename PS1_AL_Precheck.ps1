# PS1_AL_Precheck.ps1
# READ-ONLY -- no writes, no transaction.
# Annual Leave import precheck: name resolution, AL storage path evidence,
# existing ShiftEntries AL rows for each listed date range.
# Tim Nguyen excluded (not approved).

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"
Add-Type -AssemblyName "System.Data"
$cfg = Get-Content $appSettingsPath | ConvertFrom-Json
$cs  = $cfg.ConnectionStrings.DefaultConnection

function Open-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection($script:cs)
    $c.Open()
    return $c
}

function Invoke-Rows([string]$sql, [hashtable]$params = @{}) {
    $conn = Open-Conn
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($kv in $params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        $rows = @()
        $rdr  = $cmd.ExecuteReader()
        while ($rdr.Read()) {
            $row = @{}
            for ($i = 0; $i -lt $rdr.FieldCount; $i++) {
                $v = $rdr.GetValue($i)
                $rowVal = $v; if ($v -is [System.DBNull]) { $rowVal = $null }
                $row[$rdr.GetName($i)] = $rowVal
            }
            $rows += [PSCustomObject]$row
        }
        $rdr.Close()
        return $rows
    } finally { $conn.Close() }
}

# Strip Unicode combining (diacritical) marks -- equivalent to C# CompareOptions.IgnoreNonSpace.
function Strip-Accents([string]$s) {
    $n  = $s.Normalize([System.Text.NormalizationForm]::FormD)
    $sb = [System.Text.StringBuilder]::new()
    foreach ($c in $n.ToCharArray()) {
        if ([System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($c) -ne
            [System.Globalization.UnicodeCategory]::NonSpacingMark) { [void]$sb.Append($c) }
    }
    return $sb.ToString()
}

# Parse DD.MM.YYYY -> System.DateTime
function Parse-DMY([string]$dmy) {
    return [DateTime]::ParseExact($dmy, "dd.MM.yyyy",
        [System.Globalization.CultureInfo]::InvariantCulture)
}

# Count Mon-Fri days in an inclusive calendar range.
function Count-WorkDays([DateTime]$from, [DateTime]$to) {
    $cnt = 0
    for ($d = $from; $d -le $to; $d = $d.AddDays(1)) {
        if ($d.DayOfWeek -ne [DayOfWeek]::Saturday -and
            $d.DayOfWeek -ne [DayOfWeek]::Sunday) { $cnt++ }
    }
    return $cnt
}

# ── AL list (Tim Nguyen not approved -- excluded) ─────────────────────────────
# Columns: Name, From DD.MM.YYYY, To DD.MM.YYYY, ALNote
$alList = @(
    [PSCustomObject]@{ Name="Suhrab Sadieqy";           From="22.06.2026"; To="23.06.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Elaheh Ramzi";             From="26.06.2026"; To="26.06.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Elaheh Ramzi";             From="03.07.2026"; To="03.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Eva-Liane Schliwa";        From="29.06.2026"; To="29.06.2026"; ALNote="0.5 day" }
    [PSCustomObject]@{ Name="Aman Kedo";                From="02.07.2026"; To="03.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Tim Boger";                From="03.07.2026"; To="03.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Tim Boger";                From="10.07.2026"; To="10.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Viktor Winter";            From="06.07.2026"; To="17.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Timon Philippen";          From="08.07.2026"; To="17.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Marcus Rusch";             From="13.07.2026"; To="17.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Mustafa Deveci";           From="08.07.2026"; To="10.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Mustafa Deveci";           From="15.07.2026"; To="17.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Mustafa Deveci";           From="22.07.2026"; To="24.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Mustafa Deveci";           From="29.07.2026"; To="31.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Kemal Sener";              From="17.07.2026"; To="17.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Kemal Sener";              From="23.07.2026"; To="24.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Ivonne Specht";            From="27.07.2026"; To="31.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Holger Petzholdt";         From="27.07.2026"; To="31.07.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Rene Altmeyer";            From="03.08.2026"; To="07.08.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Dominik Bajic";            From="03.08.2026"; To="05.08.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Dominik Bajic";            From="10.08.2026"; To="12.08.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Senthuran Shanmugalingam"; From="07.08.2026"; To="19.08.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Aman Kedo";                From="06.08.2026"; To="07.08.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Marcus Rusch";             From="14.09.2026"; To="18.09.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Aman Kedo";                From="14.09.2026"; To="18.09.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Aman Kedo";                From="26.10.2026"; To="30.10.2026"; ALNote="" }
    [PSCustomObject]@{ Name="Aman Kedo";                From="28.12.2026"; To="30.12.2026"; ALNote="" }
)

# ══════════════════════════════════════════════════════════════════════════════
# CHECK 1 -- NAME RESOLUTION
# ══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host " CHECK 1 - NAME RESOLUTION" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host "  Loading all employees..." -NoNewline

$script:allEmp = Invoke-Rows "SELECT EmployeeId, FullName, IsActive FROM Employees ORDER BY FullName"
Write-Host " $($script:allEmp.Count) total." -ForegroundColor DarkGray
Write-Host "  Match tiers: (1) exact OrdinalIgnoreCase, (2) accent-normalized,"
Write-Host "  (3) token-prefix variant (handles 'Aman'/'Amani', 'Rene'/'Rene+accent')."
Write-Host ""

# Returns PSCustomObject: { Emp, Type ("exact"|"accent"|"variant"), Status ("OK"|"AMBIGUOUS"|"NO MATCH"), All }
function Resolve-Name([string]$listName) {
    $lStr = (Strip-Accents $listName).ToLowerInvariant()
    $lTok = @(($lStr.Trim() -split '\s+') | Where-Object { $_ -ne '' })

    # Tier 1: exact case-insensitive
    $t1 = @($script:allEmp | Where-Object {
        $null -ne $_.FullName -and [string]::Equals($_.FullName, $listName, 'OrdinalIgnoreCase')
    })
    if ($t1.Count -ge 1) {
        $st = "AMBIGUOUS"; if ($t1.Count -eq 1) { $st = "OK" }
        return [PSCustomObject]@{ Emp=$t1[0]; Type="exact"; Status=$st; All=$t1 }
    }

    # Tier 2: accent-normalized exact
    $t2 = @($script:allEmp | Where-Object {
        $null -ne $_.FullName -and
        (Strip-Accents $_.FullName).ToLowerInvariant() -eq $lStr
    })
    if ($t2.Count -ge 1) {
        $st = "AMBIGUOUS"; if ($t2.Count -eq 1) { $st = "OK" }
        return [PSCustomObject]@{ Emp=$t2[0]; Type="accent"; Status=$st; All=$t2 }
    }

    # Tier 3: token-prefix variant -- same token count, each pair one is prefix of the other.
    # Catches "Aman"/"Amani" type name truncation differences.
    $t3 = @($script:allEmp | Where-Object {
        if ($null -eq $_.FullName) { $false }
        else {
            $dTok = @(((Strip-Accents $_.FullName).ToLowerInvariant().Trim() -split '\s+') |
                      Where-Object { $_ -ne '' })
            if ($dTok.Count -ne $lTok.Count) { $false }
            else {
                $ok = $true
                for ($i = 0; $i -lt $lTok.Count; $i++) {
                    if (-not ($lTok[$i].StartsWith($dTok[$i]) -or $dTok[$i].StartsWith($lTok[$i]))) {
                        $ok = $false; break
                    }
                }
                $ok
            }
        }
    })
    if ($t3.Count -ge 1) {
        $st = "AMBIGUOUS"; if ($t3.Count -eq 1) { $st = "OK" }
        return [PSCustomObject]@{ Emp=$t3[0]; Type="variant"; Status=$st; All=$t3 }
    }

    return [PSCustomObject]@{ Emp=$null; Type=$null; Status="NO MATCH"; All=@() }
}

$distinctNames = @($alList | Select-Object -ExpandProperty Name -Unique)
$nameMap = @{}
foreach ($n in $distinctNames) { $nameMap[$n] = Resolve-Name $n }

# Print name resolution table
$h1Fmt = "{0,-35} {1,-30} {2,-12} {3,-16} {4}"
Write-Host ($h1Fmt -f "LIST NAME", "DB FullName", "EmployeeId", "Active", "Match") -ForegroundColor Cyan
Write-Host ($h1Fmt -f ("-"*35), ("-"*30), ("-"*12), ("-"*16), ("-"*8)) -ForegroundColor DarkGray

foreach ($n in ($distinctNames | Sort-Object)) {
    $r = $nameMap[$n]
    if ($r.Status -eq "OK") {
        $activeStr = "NO (inactive!)"
        if ($r.Emp.IsActive) { $activeStr = "Yes" }
        $color = "Yellow"
        if ($r.Emp.IsActive -and $r.Type -eq "exact") { $color = "Green" }
        Write-Host ($h1Fmt -f $n, $r.Emp.FullName, $r.Emp.EmployeeId, $activeStr, $r.Type) -ForegroundColor $color
    } elseif ($r.Status -eq "AMBIGUOUS") {
        Write-Host ($h1Fmt -f $n, "AMBIGUOUS ($($r.All.Count) matches)", "-", "-", $r.Type) -ForegroundColor Yellow
        foreach ($e in $r.All) {
            $actStr = "inactive"
            if ($e.IsActive) { $actStr = "active" }
            Write-Host ("    -> {0,-28} {1,-14} ({2})" -f $e.FullName, $e.EmployeeId, $actStr) -ForegroundColor DarkYellow
        }
    } else {
        Write-Host ($h1Fmt -f $n, "NO MATCH", "-", "-", "") -ForegroundColor Red
    }
}

# ══════════════════════════════════════════════════════════════════════════════
# CHECK 2 -- WHERE THE SYSTEM STORES / READS AL
# ══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host " CHECK 2 - AL STORAGE PATH (code evidence)" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "CONCLUSION" -ForegroundColor Cyan
Write-Host "  AL must be in ShiftEntries with ShiftType = 'AL'." -ForegroundColor Green
Write-Host "  The Vacations table exists but is NOT read by the coverage path." -ForegroundColor Green
Write-Host "  For a 0.5-day absence (Eva-Liane Schliwa), use ShiftType = 'HALF_AL'." -ForegroundColor Green
Write-Host ""
Write-Host "CODE EVIDENCE" -ForegroundColor Cyan
Write-Host "  AvailabilityResolver.cs:29    -- FullAbsenceTypes = { 'SL','AL','UL','PH','LPH','RESIGNED' }"
Write-Host "  AvailabilityResolver.cs:40-42 -- GetStatusAsync(): checks SickLeaves first (priority),"
Write-Host "                                   then reads ShiftEntries.ShiftType for the date."
Write-Host "  AvailabilityResolver.cs:67-92 -- GetAbsentIdsAsync() queries:"
Write-Host "       SickLeaves WHERE FirstDay <= date <= LastDay"
Write-Host "       ShiftEntries WHERE ShiftType IN FullAbsenceTypes AND ShiftDate = date"
Write-Host "  WicShiftService.cs:214-217    -- GetOpenAsync() loads WicShiftEntries (who is on-site)"
Write-Host "  WicShiftService.cs:218-223    -- GetOpenAsync() loads ShiftEntries to detect HALF_AL:"
Write-Host "       ShiftType='HALF_AL' -> contributes 0.5 to effectiveCoverage (line 278-279)"
Write-Host "  WicShiftService.cs:236        -- calls _resolver.GetAbsentIdsAsync() for absent IDs"
Write-Host "  WicShiftService.cs:469        -- GET /api/wic/open -> GetOpenAsync()"
Write-Host ""
Write-Host "  VacationService.cs / ALCalendarService.cs -- read Vacations table for planning"
Write-Host "  views; Vacations is NOT queried by AvailabilityResolver or GetOpenAsync,"
Write-Host "  so entries there have NO effect on /api/wic/open coverage counts."
Write-Host ""
Write-Host "TARGET TABLE: ShiftEntries -- live schema from DB:" -ForegroundColor Cyan
$seSchema = Invoke-Rows @"
SELECT COLUMN_NAME, DATA_TYPE,
       CHARACTER_MAXIMUM_LENGTH,
       IS_NULLABLE,
       COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'ShiftEntries'
ORDER BY ORDINAL_POSITION
"@
foreach ($col in $seSchema) {
    $len   = ""
    if ($null -ne $col.CHARACTER_MAXIMUM_LENGTH) { $len = "($($col.CHARACTER_MAXIMUM_LENGTH))" }
    $null_ = "NOT NULL"
    if ($col.IS_NULLABLE -eq "YES") { $null_ = "NULL" }
    $def   = ""
    if ($null -ne $col.COLUMN_DEFAULT) { $def = " DEFAULT $($col.COLUMN_DEFAULT)" }
    Write-Host ("    {0,-22} {1,-10}{2,-8} {3}{4}" -f $col.COLUMN_NAME, $col.DATA_TYPE, $len, $null_, $def)
}
Write-Host "  Unique constraint: (EmployeeId, ShiftDate, SourceSheet)" -ForegroundColor DarkGray
Write-Host "  Import note: SourceSheet NULL counts as a distinct value in the unique key." -ForegroundColor DarkGray
Write-Host "  A row with SourceSheet='GSD_DE' and SourceSheet='AL_IMPORT' can coexist" -ForegroundColor DarkGray
Write-Host "  for the same (EmployeeId, ShiftDate) -- only the AL one affects coverage." -ForegroundColor DarkGray

# ══════════════════════════════════════════════════════════════════════════════
# CHECK 3 -- WHAT ALREADY EXISTS IN ShiftEntries
# ══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host " CHECK 3 - EXISTING AL ENTRIES IN ShiftEntries" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host "  Query per entry: ShiftType IN ('AL','UL','HALF_AL') for EmployeeId + range." -ForegroundColor DarkGray
Write-Host "  'workdays' = Mon-Fri count in range (for FULLY PRESENT indicator)." -ForegroundColor DarkGray
Write-Host ""

$h3Fmt = "{0,-35} {1,-11} {2,-11} {3,-8} {4}"
Write-Host ($h3Fmt -f "NAME", "FROM", "TO", "NOTE", "STATUS") -ForegroundColor Cyan
Write-Host ($h3Fmt -f ("-"*35), ("-"*11), ("-"*11), ("-"*8), ("-"*46)) -ForegroundColor DarkGray

$cntNew    = 0
$cntHit    = 0
$cntSkipNM = 0

foreach ($entry in $alList) {
    $r = $nameMap[$entry.Name]
    if ($r.Status -ne "OK") {
        Write-Host ($h3Fmt -f $entry.Name, $entry.From, $entry.To, $entry.ALNote, "SKIP - name not resolved") -ForegroundColor Red
        $cntSkipNM++
        continue
    }

    $empId   = $r.Emp.EmployeeId
    $fromDt  = Parse-DMY $entry.From
    $toDt    = Parse-DMY $entry.To
    $fromSql = $fromDt.ToString("yyyy-MM-dd")
    $toSql   = $toDt.ToString("yyyy-MM-dd")

    $existing = Invoke-Rows @"
SELECT ShiftDate, ShiftType, ISNULL(SourceSheet, '(null)') AS SourceSheet
FROM ShiftEntries
WHERE EmployeeId = @empId
  AND ShiftDate >= @f
  AND ShiftDate <= @t
  AND ShiftType IN ('AL', 'UL', 'HALF_AL')
ORDER BY ShiftDate
"@ @{ empId=$empId; f=$fromSql; t=$toSql }

    if ($existing.Count -eq 0) {
        Write-Host ($h3Fmt -f $entry.Name, $entry.From, $entry.To, $entry.ALNote, "MISSING (safe to import)") -ForegroundColor Green
        $cntNew++
    } else {
        $workDays  = Count-WorkDays $fromDt $toDt
        $indicator = "PARTIAL ($($existing.Count) of $workDays workdays found)"
        if ($existing.Count -ge $workDays -and $workDays -gt 0) {
            $indicator = "FULLY PRESENT ($($existing.Count) rows)"
        }
        Write-Host ($h3Fmt -f $entry.Name, $entry.From, $entry.To, $entry.ALNote, "$indicator -- details:") -ForegroundColor Yellow
        foreach ($ex in $existing) {
            $dateStr = $ex.ShiftDate.ToString("dd.MM.yyyy")
            Write-Host ("    $dateStr  ShiftType=$($ex.ShiftType)  SourceSheet=$($ex.SourceSheet)") -ForegroundColor DarkYellow
        }
        $cntHit++
    }
}

# ══════════════════════════════════════════════════════════════════════════════
# SUMMARY
# ══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host " SUMMARY" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta

$cntDistinct    = $distinctNames.Count
$cntNamesOK     = ($nameMap.Values | Where-Object { $_.Status -eq "OK"        }).Count
$cntNamesNM     = ($nameMap.Values | Where-Object { $_.Status -eq "NO MATCH"  }).Count
$cntNamesAmb    = ($nameMap.Values | Where-Object { $_.Status -eq "AMBIGUOUS" }).Count
$cntNamesNotExact = ($nameMap.Values | Where-Object { $_.Status -eq "OK" -and $_.Type -ne "exact" }).Count

Write-Host ""
Write-Host "  CHECK 1 -- Names ($cntDistinct distinct):" -ForegroundColor Cyan
Write-Host ("    Resolved (OK)            : {0}" -f $cntNamesOK) -ForegroundColor $(if ($cntNamesOK -eq $cntDistinct) { "Green" } else { "Yellow" })
Write-Host ("      accent/variant matches : {0}  (DB spelling differs from list -- verify)" -f $cntNamesNotExact) -ForegroundColor $(if ($cntNamesNotExact -eq 0) { "Green" } else { "Yellow" })
Write-Host ("    NO MATCH                 : {0}" -f $cntNamesNM)  -ForegroundColor $(if ($cntNamesNM  -eq 0) { "Green" } else { "Red" })
Write-Host ("    AMBIGUOUS                : {0}" -f $cntNamesAmb) -ForegroundColor $(if ($cntNamesAmb -eq 0) { "Green" } else { "Yellow" })

Write-Host ""
Write-Host "  CHECK 2 -- Target table: ShiftEntries" -ForegroundColor Cyan
Write-Host "    ShiftType='AL' for full day; ShiftType='HALF_AL' for 0.5-day entries."
Write-Host "    Vacations table NOT in coverage path -- do not import there."

Write-Host ""
Write-Host "  CHECK 3 -- AL entries ($($alList.Count) rows, Tim Nguyen excluded):" -ForegroundColor Cyan
Write-Host ("    New (not in DB)          : {0}" -f $cntNew)     -ForegroundColor $(if ($cntNew    -gt 0) { "Green"  } else { "DarkGray" })
Write-Host ("    Already in DB            : {0}" -f $cntHit)     -ForegroundColor $(if ($cntHit    -eq 0) { "Green"  } else { "Yellow"   })
Write-Host ("    Skipped (name no match)  : {0}" -f $cntSkipNM)  -ForegroundColor $(if ($cntSkipNM -eq 0) { "Green"  } else { "Red"      })

Write-Host ""
Write-Host "READ-ONLY complete. No data was written." -ForegroundColor Green
