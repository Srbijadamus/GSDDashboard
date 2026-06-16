# PS1_9_LegacyCodeMap.ps1
# Adds LocationCodeLegacy column to WicLocations (idempotent IF COL_LENGTH guard),
# then stamps each row with the matching old-style code from WicAgentAssignments.
# RENDSBURG already matches directly in both tables - no legacy entry needed (expected count = 42).
# No here-strings. No Unicode outside of SQL parameters. Run from powershell.exe 5.1.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function Exec($sql, $label) {
    Write-Host ""
    Write-Host ">>> $label" -ForegroundColor Cyan
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        $cmd.CommandTimeout = 30
        $n = $cmd.ExecuteNonQuery()
        $conn.Close()
        Write-Host "OK ($n rows affected)" -ForegroundColor Green
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
    }
}

function Query($sql, $label) {
    Write-Host ""
    Write-Host ">>> $label" -ForegroundColor Cyan
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        $cmd.CommandTimeout = 30
        $r = $cmd.ExecuteReader()
        $rows = @()
        while ($r.Read()) {
            $row = [ordered]@{}
            for ($i = 0; $i -lt $r.FieldCount; $i++) {
                $v = $r.GetValue($i)
                $row[$r.GetName($i)] = if ($v -is [DBNull]) { "NULL" } else { $v }
            }
            $rows += [pscustomobject]$row
        }
        $r.Close()
        $conn.Close()
        if ($rows.Count -eq 0) {
            Write-Host "(no rows)" -ForegroundColor Yellow
        } else {
            $rows | Format-Table -AutoSize -Wrap
        }
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
    }
}

# --- Step 1: Add column if missing -------------------------------------------

$alterSql = "IF COL_LENGTH('WicLocations','LocationCodeLegacy') IS NULL ALTER TABLE WicLocations ADD LocationCodeLegacy NVARCHAR(50) NULL"
Exec $alterSql "Add LocationCodeLegacy column (no-op if exists)"

# --- Step 2: Populate 42 legacy-code mappings --------------------------------
# Key   = old-style code stored in WicAgentAssignments
# Value = LIKE pattern against WicLocations.LocationCode (PLZ-anchored, ASCII-safe)
# Demmin patterns include city segment to distinguish the two WICs sharing PLZ 17109.
# NL patterns use the numeric postal prefix which is unique per city.

$legacyMap = [ordered]@{
    "DE_Arnsberg"        = "DE~59821%"
    "DE_Augsburg"        = "DE~86150%"
    "DE_Bamberg"         = "DE~96052%"
    "DE_Berlin_Gauss"    = "DE~10589%"
    "DE_Berlin_Kopenick" = "DE~10179%"
    "DE_Brokdorf"        = "DE~25576%"
    "DE_Demmin_Hanse"    = "DE~17109~ Demmin~%"
    "DE_Demmin_Wold"     = "DE~17109~Demmin~Wold%"
    "DE_Dortmund"        = "DE~44139%"
    "DE_Emmerthal"       = "DE~31860%"
    "DE_Essen_BP1"       = "DE~45131%"
    "DE_Essen_TK1"       = "DE~45143%"
    "DE_Essenbach"       = "DE~84051%"
    "DE_Furstenwalde"    = "DE~15517%"
    "DE_Grafenrheinfeld" = "DE~97506%"
    "DE_Halle"           = "DE~06112%"
    "DE_Hamburg"         = "DE~20537%"
    "DE_Hannover"        = "DE~30459%"
    "DE_Helmstedt"       = "DE~38350%"
    "DE_Landshut"        = "DE~84036%"
    "DE_Markkleeberg"    = "DE~04416%"
    "DE_Mulheim"         = "DE~45476%"
    "DE_Munchen"         = "DE~80634%"
    "DE_Munster"         = "DE~48163%"
    "DE_NeuIsenburg"     = "DE~63263%"
    "DE_Neuss"           = "DE~41460%"
    "DE_Osnabruck"       = "DE~49074%"
    "DE_Pfaffenhofen"    = "DE~85276%"
    "DE_Potsdam"         = "DE~14467%"
    "DE_Quickborn"       = "DE~25451%"
    "DE_Recklinghausen"  = "DE~45661%"
    "DE_Regensburg"      = "DE~93049%"
    "DE_Saarbrucken"     = "DE~66121%"
    "DE_Saffig"          = "DE~56648%"
    "DE_Salzgitter"      = "DE~38226%"
    "DE_Siegen"          = "DE~57072%"
    "DE_Stade"           = "DE~21683%"
    "DE_Stadland"        = "DE~26935%"
    "DE_Trier"           = "DE~54294%"
    "DE_Wesel"           = "DE~46483%"
    "NL_Denbosch"        = "NL~5211%"
    "NL_Zwolle"          = "NL~8041%"
}

Write-Host ""
Write-Host ">>> Updating LocationCodeLegacy ($($legacyMap.Count) entries)" -ForegroundColor Cyan

$totalUpdated = 0
$noMatch = @()

$sqlUpdate = "UPDATE WicLocations SET LocationCodeLegacy = @legacy WHERE LocationCode LIKE @pattern AND (LocationCodeLegacy IS NULL OR LocationCodeLegacy <> @legacy)"
$sqlCheck  = "SELECT COUNT(*) FROM WicLocations WHERE LocationCode LIKE @pattern"

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

foreach ($legacy in $legacyMap.Keys) {
    $pattern = $legacyMap[$legacy]

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sqlUpdate
    $cmd.Parameters.AddWithValue("@legacy",  $legacy)  | Out-Null
    $cmd.Parameters.AddWithValue("@pattern", $pattern) | Out-Null
    $n = $cmd.ExecuteNonQuery()

    if ($n -gt 0) {
        Write-Host ("  {0,-24} -> {1,-30} ({2} row)" -f $legacy, $pattern, $n) -ForegroundColor Green
        $totalUpdated += $n
    } else {
        $chk = $conn.CreateCommand()
        $chk.CommandText = $sqlCheck
        $chk.Parameters.AddWithValue("@pattern", $pattern) | Out-Null
        $exists = [int]$chk.ExecuteScalar()
        if ($exists -gt 0) {
            Write-Host ("  {0,-24} -> already set (no change)" -f $legacy) -ForegroundColor DarkGreen
        } else {
            Write-Host ("  {0,-24} -> NO MATCH for pattern $pattern" -f $legacy) -ForegroundColor Yellow
            $noMatch += $legacy
        }
    }
}

$conn.Close()

Write-Host ""
Write-Host "Rows updated this run: $totalUpdated" -ForegroundColor Green

if ($noMatch.Count -gt 0) {
    Write-Host ""
    Write-Host "WARNING: $($noMatch.Count) pattern(s) matched no WicLocations row:" -ForegroundColor Yellow
    foreach ($m in $noMatch) {
        Write-Host "  $m -> $($legacyMap[$m])" -ForegroundColor Yellow
    }
    Write-Host "Run: SELECT LocationCode FROM WicLocations ORDER BY LocationCode" -ForegroundColor Yellow
    Write-Host "Find the correct PLZ prefix, fix the hashtable entry, and re-run." -ForegroundColor Yellow
}

# --- Step 3: Verify ----------------------------------------------------------

$sqlV1 = "SELECT COUNT(*) AS LocationsWithLegacyCode FROM WicLocations WHERE LocationCodeLegacy IS NOT NULL"

$sqlV2 = "SELECT COUNT(*) AS UnresolvedAssignmentRows FROM WicAgentAssignments waa WHERE NOT EXISTS (SELECT 1 FROM WicLocations wl WHERE wl.LocationCode = waa.LocationCode OR wl.LocationCodeLegacy = waa.LocationCode)"

$sqlV3 = "SELECT DISTINCT waa.LocationCode AS StillUnresolved FROM WicAgentAssignments waa WHERE NOT EXISTS (SELECT 1 FROM WicLocations wl WHERE wl.LocationCode = waa.LocationCode OR wl.LocationCodeLegacy = waa.LocationCode) ORDER BY waa.LocationCode"

Query $sqlV1 "WicLocations rows with LocationCodeLegacy set (expected: 42)"
Query $sqlV2 "Unresolved WicAgentAssignments rows after mapping (expected: 0)"
Query $sqlV3 "Remaining unresolved old codes by name (should be empty)"

Write-Host ""
Write-Host "=== PS1_9 complete. Rebuild the backend to activate LocationCodeLegacy joins. ===" -ForegroundColor Green
