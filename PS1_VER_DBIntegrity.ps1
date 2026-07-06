# PS1_VER_DBIntegrity.ps1
# Section 2 - DB Integrity queries (READ-ONLY).
# Checks orphans, name-keyed refs, unmapped SupportLocations, inactive-in-assignments, duplicates.
# Run manually and paste output into VERIFICATION_REPORT.md.

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"

Add-Type -AssemblyName "System.Data"
$cfg = Get-Content $appSettingsPath | ConvertFrom-Json
$cs  = $cfg.ConnectionStrings.DefaultConnection

function Invoke-Rows([string]$sql, [hashtable]$params = @{}) {
    $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($kv in $params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        $rows = @()
        $rdr = $cmd.ExecuteReader()
        while ($rdr.Read()) {
            $row = @{}
            for ($i = 0; $i -lt $rdr.FieldCount; $i++) {
                $v = $rdr.GetValue($i)
                $row[$rdr.GetName($i)] = if ($v -is [System.DBNull]) { $null } else { $v }
            }
            $rows += [PSCustomObject]$row
        }
        $rdr.Close()
        return $rows
    } finally { $conn.Close() }
}

function Scalar([string]$sql, [hashtable]$params = @{}) {
    $rows = Invoke-Rows $sql $params
    if ($rows.Count -eq 0) { return 0 }
    $first = $rows[0]
    $prop = ($first | Get-Member -MemberType NoteProperty | Select-Object -First 1).Name
    $v = $first.$prop
    if ($null -eq $v) { return 0 }
    return [int]$v
}

function Section([string]$title) {
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "  $title" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
}

function SubSection([string]$title) {
    Write-Host ""
    Write-Host "--- $title ---" -ForegroundColor Yellow
}

function PrintRows($rows, [int]$maxDisplay = 10) {
    if ($null -eq $rows -or $rows.Count -eq 0) {
        Write-Host "  (none)" -ForegroundColor Green
        return
    }
    $shown = [Math]::Min($rows.Count, $maxDisplay)
    for ($i = 0; $i -lt $shown; $i++) {
        $r = $rows[$i]
        $parts = @()
        foreach ($p in ($r | Get-Member -MemberType NoteProperty).Name) {
            $parts += "${p}=$($r.$p)"
        }
        Write-Host ("  " + ($parts -join "  ")) -ForegroundColor White
    }
    if ($rows.Count -gt $maxDisplay) {
        Write-Host ("  ... and {0} more rows (truncated at {1})" -f ($rows.Count - $maxDisplay), $maxDisplay) -ForegroundColor DarkYellow
    }
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Magenta
Write-Host " DB INTEGRITY CHECK - READ ONLY" -ForegroundColor Magenta
Write-Host "==========================================" -ForegroundColor Magenta

# ══════════════════════════════════════════════════════
# CHECK 1: Orphan ShiftEntries (EmployeeId not in Employees)
# ══════════════════════════════════════════════════════
Section "CHECK 1: Orphan ShiftEntries"

$orphanShifts = Invoke-Rows @"
SELECT TOP 20 s.Id, s.EmployeeId, s.ShiftDate, s.ShiftType
FROM ShiftEntries s
WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)
ORDER BY s.ShiftDate DESC
"@
$orphanShiftCount = Scalar "SELECT COUNT(*) AS C FROM ShiftEntries s WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)"
Write-Host ("Total orphan ShiftEntries: {0}" -f $orphanShiftCount) -ForegroundColor $(if ($orphanShiftCount -eq 0) { "Green" } else { "Red" })
PrintRows $orphanShifts

# ══════════════════════════════════════════════════════
# CHECK 2: Orphan WicShiftEntries (EmployeeId not in Employees)
# ══════════════════════════════════════════════════════
Section "CHECK 2: Orphan WicShiftEntries"

$orphanWicShifts = Invoke-Rows @"
SELECT TOP 20 w.Id, w.EmployeeId, w.ShiftDate, w.SupportLocation
FROM WicShiftEntries w
WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = w.EmployeeId)
ORDER BY w.ShiftDate DESC
"@
$orphanWicCount = Scalar "SELECT COUNT(*) AS C FROM WicShiftEntries w WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = w.EmployeeId)"
Write-Host ("Total orphan WicShiftEntries: {0}" -f $orphanWicCount) -ForegroundColor $(if ($orphanWicCount -eq 0) { "Green" } else { "Red" })
PrintRows $orphanWicShifts

# ══════════════════════════════════════════════════════
# CHECK 3: WicAgentAssignments name-keyed refs with no match in Employees.FullName
# ══════════════════════════════════════════════════════
Section "CHECK 3: WicAgentAssignments - unresolved EmployeeName"

$unmappedAssign = Invoke-Rows @"
SELECT TOP 20 waa.Id, waa.EmployeeName, waa.LocationCode, waa.AssignmentType, waa.IsActive
FROM WicAgentAssignments waa
WHERE NOT EXISTS (
    SELECT 1 FROM Employees e WHERE e.FullName = waa.EmployeeName
)
ORDER BY waa.EmployeeName
"@
$unmappedAssignCount = Scalar "SELECT COUNT(*) AS C FROM WicAgentAssignments waa WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = waa.EmployeeName)"
Write-Host ("Total WicAgentAssignments with unresolved name: {0}" -f $unmappedAssignCount) -ForegroundColor $(if ($unmappedAssignCount -eq 0) { "Green" } else { "Yellow" })
PrintRows $unmappedAssign

# ══════════════════════════════════════════════════════
# CHECK 4: AgentReachableCities name-keyed refs with no match in Employees.FullName
# ══════════════════════════════════════════════════════
Section "CHECK 4: AgentReachableCities - unresolved EmployeeName"

$unmappedCities = Invoke-Rows @"
SELECT TOP 20 arc.Id, arc.EmployeeName, arc.EmployeeId, arc.City
FROM AgentReachableCities arc
WHERE arc.EmployeeName IS NOT NULL
  AND arc.EmployeeName <> ''
  AND NOT EXISTS (
    SELECT 1 FROM Employees e WHERE e.FullName = arc.EmployeeName
  )
ORDER BY arc.EmployeeName
"@
$unmappedCitiesCount = Scalar "SELECT COUNT(*) AS C FROM AgentReachableCities arc WHERE arc.EmployeeName IS NOT NULL AND arc.EmployeeName <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = arc.EmployeeName)"
Write-Host ("Total AgentReachableCities with unresolved name: {0}" -f $unmappedCitiesCount) -ForegroundColor $(if ($unmappedCitiesCount -eq 0) { "Green" } else { "Yellow" })
PrintRows $unmappedCities

# ══════════════════════════════════════════════════════
# CHECK 5: WicPipeline PrimaryAgent / BackupAgent unresolved
# ══════════════════════════════════════════════════════
Section "CHECK 5: WicPipeline - unresolved PrimaryAgent / BackupAgent"

$unmappedPipe = Invoke-Rows @"
SELECT TOP 20 wp.Id, wp.LocationCode, wp.PipelineDate, wp.PrimaryAgent, wp.BackupAgent
FROM WicPipeline wp
WHERE (
    wp.PrimaryAgent IS NOT NULL
    AND wp.PrimaryAgent <> ''
    AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.PrimaryAgent)
) OR (
    wp.BackupAgent IS NOT NULL
    AND wp.BackupAgent <> ''
    AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.BackupAgent)
)
ORDER BY wp.PipelineDate DESC
"@
$unmappedPipeCount = Scalar @"
SELECT COUNT(*) AS C FROM WicPipeline wp
WHERE (wp.PrimaryAgent IS NOT NULL AND wp.PrimaryAgent <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.PrimaryAgent))
   OR (wp.BackupAgent  IS NOT NULL AND wp.BackupAgent  <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.BackupAgent))
"@
Write-Host ("Total WicPipeline rows with unresolved agent name: {0}" -f $unmappedPipeCount) -ForegroundColor $(if ($unmappedPipeCount -eq 0) { "Green" } else { "Yellow" })
PrintRows $unmappedPipe

# ══════════════════════════════════════════════════════
# CHECK 6: TrainingSchedule SuggestedBy / ConfirmedBy unresolved
# ══════════════════════════════════════════════════════
Section "CHECK 6: TrainingSchedule - unresolved SuggestedBy / ConfirmedBy"

$unmappedTraining = Invoke-Rows @"
SELECT TOP 20 ts.Id, ts.TopicId, ts.ScheduledDate, ts.SuggestedBy, ts.ConfirmedBy
FROM TrainingSchedule ts
WHERE (
    ts.SuggestedBy IS NOT NULL
    AND ts.SuggestedBy <> ''
    AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = ts.SuggestedBy)
) OR (
    ts.ConfirmedBy IS NOT NULL
    AND ts.ConfirmedBy <> ''
    AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = ts.ConfirmedBy)
)
ORDER BY ts.ScheduledDate DESC
"@
$unmappedTrainingCount = Scalar @"
SELECT COUNT(*) AS C FROM TrainingSchedule ts
WHERE (ts.SuggestedBy IS NOT NULL AND ts.SuggestedBy <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = ts.SuggestedBy))
   OR (ts.ConfirmedBy IS NOT NULL AND ts.ConfirmedBy <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = ts.ConfirmedBy))
"@
Write-Host ("Total TrainingSchedule rows with unresolved name: {0}" -f $unmappedTrainingCount) -ForegroundColor $(if ($unmappedTrainingCount -eq 0) { "Green" } else { "Yellow" })
PrintRows $unmappedTraining

# ══════════════════════════════════════════════════════
# CHECK 7: WicShiftEntries.SupportLocation unmapped to any WicLocation
# ══════════════════════════════════════════════════════
Section "CHECK 7: WicShiftEntries - unmapped SupportLocation (WicLocationMatcher logic)"

# Replicate WicLocationMatcher._aliases (IgnoreCase+IgnoreNonSpace) in PowerShell.
# Keys are the SupportLocation values as they appear in the DB; values are LocationCode targets.
$locAliases = @{
    "Essen BP1"              = "DE_Essen_BP1"
    "Essen TK1"              = "DE_Essen_TK1"
    "Halle"                  = "DE_Halle"
    "Berlin - Gaussstr"      = "DE_Berlin_Gauss"
    "Furstenwalde"           = "DE_Furstenwalde"
    "Munchen"                = "DE_Munchen"
    "Osnabruck"              = "DE_Osnabruck"
    "Saarbrucken"            = "DE_Saarbrucken"
    "Demmin - Am Hanseufer"  = "DE_Demmin_Hanse"
    "Denbosch"               = "NL_Denbosch"
    "Augsburg"               = "DE_Augsburg"
    "Bamberg"                = "DE_Bamberg"
    "Brokdorf"               = "DE_Brokdorf"
    "Dortmund"               = "DE_Dortmund"
    "Emmerthal"              = "DE_Emmerthal"
    "Essenbach"              = "DE_Essenbach"
    "Grafenrheinfeld"        = "DE_Grafenrheinfeld"
    "Hamburg"                = "DE_Hamburg"
    "Hannover"               = "DE_Hannover"
    "Helmstedt"              = "DE_Helmstedt"
    "Neu-Isenburg"           = "DE_NeuIsenburg"
    "Pfaffenhofen"           = "PFAFFENHOFEN"
    "Potsdam"                = "DE_Potsdam"
    "Quickborn"              = "DE_Quickborn"
    "Regensburg"             = "DE_Regensburg"
    "Rendsburg"              = "RENDSBURG"
    "Salzgitter"             = "DE_Salzgitter"
    "Stade"                  = "DE_Stade"
    "Stadland"               = "DE_Stadland"
    "Zwolle"                 = "NL_Zwolle"
    "Essen (Bruesseler Pl.)" = "DE_Essen_BP1"
    "Essen (Brusseler Pl.)"  = "DE_Essen_BP1"
    "Essen (ThyssenKrupp)"   = "DE_Essen_TK1"
    "Demmin (Am Hanseufer)"  = "DE_Demmin_Hanse"
    "Berlin (Koepenicker)"   = "DE_Berlin_Kopenick"
    "Berlin (Kopenicker)"    = "DE_Berlin_Kopenick"
    "Berlin - Kopenicker"    = "DE_Berlin_Kopenick"
    "Essen_BP1"              = "DE_Essen_BP1"
    "Essen_TK1"              = "DE_Essen_TK1"
    "Demmin_Wold"            = "DE_Demmin_Wold"
    "Demmin_Hanse"           = "DE_Demmin_Hanse"
    # Bucket C: post-decode forms - matched after Repair-DoubleEncoding corrects the input.
    # sharp-s key ([char]0xDF): IgnoreNonSpace strips combining diacritics only; it cannot
    # equate sharp-s to the "ss" in the existing "Berlin - Gaussstr" key.
    "Berlin - Gau$([char]0xDF)str."  = "DE_Berlin_Gauss"
    # ASCII key: Strip-Accents resolves the [char]0xFC form that Repair-DoubleEncoding produces.
    "Berlin - Bruckenstrasse"        = "DE_Berlin_Kopenick"
}

# Strip Unicode non-spacing marks (accents) for IgnoreNonSpace-equivalent compare.
function Strip-Accents([string]$s) {
    $n = $s.Normalize([System.Text.NormalizationForm]::FormD)
    $sb = [System.Text.StringBuilder]::new()
    foreach ($c in $n.ToCharArray()) {
        if ([System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($c) -ne
            [System.Globalization.UnicodeCategory]::NonSpacingMark) { [void]$sb.Append($c) }
    }
    return $sb.ToString()
}

# Mirror of WicLocationMatcher.RepairDoubleEncoding() - decodes Windows-1252 mis-reads
# of UTF-8 two-byte sequences stored in the DB.  All non-ASCII via [char]0x.. codes so
# this file stays ASCII-only.  Guard: [char]0xC3 (Atilde) is the lead of every corrupt
# pair; it never appears in a legitimate German location name.
function Repair-DoubleEncoding([string]$s) {
    if (-not $s.Contains("$([char]0xC3)")) { return $s }
    # Corrupt pair (0xC3 + W1252 glyph for 2nd byte) -> correct Unicode char.
    $s = $s.Replace("$([char]0xC3)$([char]0xBC)", "$([char]0xFC)")    # u-umlaut  0xC3 0xBC
    $s = $s.Replace("$([char]0xC3)$([char]0xB6)", "$([char]0xF6)")    # o-umlaut  0xC3 0xB6
    $s = $s.Replace("$([char]0xC3)$([char]0xA4)", "$([char]0xE4)")    # a-umlaut  0xC3 0xA4
    $s = $s.Replace("$([char]0xC3)$([char]0x178)", "$([char]0xDF)")   # sharp-s   0xC3 0x9F (W1252 0x9F=U+0178)
    $s = $s.Replace("$([char]0xC3)$([char]0x201E)", "$([char]0xC4)")  # A-umlaut  0xC3 0x84 (W1252 0x84=U+201E)
    $s = $s.Replace("$([char]0xC3)$([char]0x2013)", "$([char]0xD6)")  # O-umlaut  0xC3 0x96 (W1252 0x96=U+2013)
    $s = $s.Replace("$([char]0xC3)$([char]0x153)", "$([char]0xDC)")   # U-umlaut  0xC3 0x9C (W1252 0x9C=U+0153)
    $s = $s.Replace("$([char]0xC3)$([char]0xA9)", "$([char]0xE9)")    # e-acute   0xC3 0xA9
    return $s
}

$wicLocs  = Invoke-Rows "SELECT LocationCode, DisplayName, City, LocationCodeLegacy FROM WicLocations"
$slRaw    = Invoke-Rows @"
SELECT SupportLocation, COUNT(*) AS Cnt
FROM WicShiftEntries
WHERE SupportLocation IS NOT NULL
  AND SupportLocation <> ''
  AND SupportLocation <> 'Global Service Desk'
GROUP BY SupportLocation
ORDER BY COUNT(*) DESC
"@

$unmapped7 = @()
foreach ($row in $slRaw) {
    $sl    = Repair-DoubleEncoding $row.SupportLocation
    $slN   = (Strip-Accents $sl).ToLowerInvariant()
    $found = $false
    foreach ($loc in $wicLocs) {
        # 1. Direct DisplayName match
        if ([string]::Equals($sl, $loc.DisplayName, 'OrdinalIgnoreCase')) { $found = $true; break }
        # 2. City match
        if ($null -ne $loc.City -and [string]::Equals($sl, $loc.City, 'OrdinalIgnoreCase')) { $found = $true; break }
        # 3. Alias map lookup (IgnoreNonSpace: compare normalized forms)
        foreach ($kv in $locAliases.GetEnumerator()) {
            if ([string]::Equals((Strip-Accents $kv.Key).ToLowerInvariant(), $slN, 'Ordinal') -and
                ([string]::Equals($kv.Value, $loc.LocationCode, 'OrdinalIgnoreCase') -or
                 ($null -ne $loc.LocationCodeLegacy -and [string]::Equals($kv.Value, $loc.LocationCodeLegacy, 'OrdinalIgnoreCase')))) {
                $found = $true; break
            }
        }
        if ($found) { break }
    }
    if (-not $found) { $unmapped7 += $row }
}

$unmappedLocCount = $unmapped7.Count
Write-Host ("Distinct unmapped SupportLocation values (WicLocationMatcher logic): {0}" -f $unmappedLocCount) -ForegroundColor $(if ($unmappedLocCount -eq 0) { "Green" } else { "Yellow" })
foreach ($r in $unmapped7) {
    Write-Host ("  SupportLocation={0,-50} Cnt={1}" -f $r.SupportLocation, $r.Cnt) -ForegroundColor White
}

# ══════════════════════════════════════════════════════
# CHECK 8: IsActive=0 employees appearing in coverage/assignment tables
# ══════════════════════════════════════════════════════
Section "CHECK 8: Inactive employees in assignment/coverage tables"

SubSection "WicAgentAssignments (IsActive rows for inactive employees)"
$inactiveAssign = Invoke-Rows @"
SELECT TOP 20 waa.EmployeeName, waa.LocationCode, waa.AssignmentType, e.EmployeeId, e.IsActive
FROM WicAgentAssignments waa
JOIN Employees e ON e.FullName = waa.EmployeeName
WHERE e.IsActive = 0
  AND waa.IsActive = 1
ORDER BY waa.EmployeeName
"@
$inactiveAssignCount = Scalar @"
SELECT COUNT(*) AS C FROM WicAgentAssignments waa
JOIN Employees e ON e.FullName = waa.EmployeeName
WHERE e.IsActive = 0 AND waa.IsActive = 1
"@
Write-Host ("Active WicAgentAssignments for inactive employees: {0}" -f $inactiveAssignCount) -ForegroundColor $(if ($inactiveAssignCount -eq 0) { "Green" } else { "Yellow" })
PrintRows $inactiveAssign

SubSection "AgentReachableCities for inactive employees"
$inactiveCities = Invoke-Rows @"
SELECT TOP 20 arc.EmployeeName, arc.City, e.EmployeeId, e.IsActive
FROM AgentReachableCities arc
JOIN Employees e ON e.FullName = arc.EmployeeName
WHERE e.IsActive = 0
ORDER BY arc.EmployeeName
"@
$inactiveCitiesCount = Scalar @"
SELECT COUNT(*) AS C FROM AgentReachableCities arc
JOIN Employees e ON e.FullName = arc.EmployeeName
WHERE e.IsActive = 0
"@
Write-Host ("AgentReachableCities rows for inactive employees: {0}" -f $inactiveCitiesCount) -ForegroundColor $(if ($inactiveCitiesCount -eq 0) { "Green" } else { "Yellow" })
PrintRows $inactiveCities

# ══════════════════════════════════════════════════════
# CHECK 9: Duplicate employees (same FullName, different EmployeeId)
# ══════════════════════════════════════════════════════
Section "CHECK 9: Duplicate FullName (different EmployeeId)"

$dupNames = Invoke-Rows @"
SELECT FullName, COUNT(*) AS Cnt, STRING_AGG(EmployeeId, ', ') AS EmployeeIds
FROM Employees
WHERE FullName IS NOT NULL
GROUP BY FullName
HAVING COUNT(*) > 1
ORDER BY FullName
"@
Write-Host ("Duplicate FullName groups: {0}" -f $dupNames.Count) -ForegroundColor $(if ($dupNames.Count -eq 0) { "Green" } else { "Red" })
PrintRows $dupNames

# ══════════════════════════════════════════════════════
# CHECK 10: Duplicate PrimaryKid (different EmployeeId)
# ══════════════════════════════════════════════════════
Section "CHECK 10: Duplicate PrimaryKid (different EmployeeId)"

$dupKids = Invoke-Rows @"
SELECT PrimaryKid, COUNT(*) AS Cnt, STRING_AGG(EmployeeId, ', ') AS EmployeeIds, STRING_AGG(FullName, ', ') AS Names
FROM Employees
WHERE PrimaryKid IS NOT NULL AND PrimaryKid <> ''
GROUP BY PrimaryKid
HAVING COUNT(*) > 1
ORDER BY PrimaryKid
"@
Write-Host ("Duplicate PrimaryKid groups: {0}" -f $dupKids.Count) -ForegroundColor $(if ($dupKids.Count -eq 0) { "Green" } else { "Red" })
PrintRows $dupKids

# ══════════════════════════════════════════════════════
# SUMMARY
# ══════════════════════════════════════════════════════
Section "SUMMARY"

$issues = @()
if ($orphanShiftCount   -gt 0) { $issues += "Orphan ShiftEntries: $orphanShiftCount" }
if ($orphanWicCount     -gt 0) { $issues += "Orphan WicShiftEntries: $orphanWicCount" }
if ($unmappedAssignCount -gt 0) { $issues += "WicAgentAssignments unresolved names: $unmappedAssignCount" }
if ($unmappedCitiesCount -gt 0) { $issues += "AgentReachableCities unresolved names: $unmappedCitiesCount" }
if ($unmappedPipeCount  -gt 0) { $issues += "WicPipeline unresolved agent names: $unmappedPipeCount" }
if ($unmappedTrainingCount -gt 0) { $issues += "TrainingSchedule unresolved names: $unmappedTrainingCount" }
if ($unmappedLocCount   -gt 0) { $issues += "Unmapped SupportLocation values: $unmappedLocCount" }
if ($inactiveAssignCount -gt 0) { $issues += "Active WicAgentAssignments for inactive employees: $inactiveAssignCount" }
if ($inactiveCitiesCount -gt 0) { $issues += "AgentReachableCities for inactive employees: $inactiveCitiesCount" }
if ($dupNames.Count     -gt 0) { $issues += "Duplicate FullName groups: $($dupNames.Count)" }
if ($dupKids.Count      -gt 0) { $issues += "Duplicate PrimaryKid groups: $($dupKids.Count)" }

if ($issues.Count -eq 0) {
    Write-Host "All checks PASSED - no integrity issues found." -ForegroundColor Green
} else {
    Write-Host "Issues found:" -ForegroundColor Red
    foreach ($issue in $issues) { Write-Host "  - $issue" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "DB Integrity check complete." -ForegroundColor Cyan
Write-Host ""
