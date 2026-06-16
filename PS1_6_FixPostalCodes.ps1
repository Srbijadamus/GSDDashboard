# PS1_6_FixPostalCodes.ps1
# Fixes two problems found by PS1_5:
#   1. PostalCode is NULL for all 43 rows — extract from LocationCode (format DE~PLZ~City~Addr)
#   2. Bundesland UPDATE matched 0 rows because it filtered on PostalCode
# After this script: PostalCode populated for all DE~... rows, Bundesland populated for all DE locations.
# Run from PowerShell 7.6. No git commands.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function Exec($sql, $label) {
    Write-Host "`n>>> $label" -ForegroundColor Cyan
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 30
        $rows = $cmd.ExecuteNonQuery()
        $conn.Close()
        Write-Host "OK ($rows row(s) affected)" -ForegroundColor Green
        return $rows
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
        return -1
    }
}

function Query($sql, $label) {
    Write-Host "`n>>> $label" -ForegroundColor Cyan
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 30
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
        $r.Close(); $conn.Close()
        if ($rows.Count -eq 0) { Write-Host "(no rows)" -ForegroundColor Yellow }
        else { $rows | Format-Table -AutoSize -Wrap }
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
    }
}

# ─── Step 1: Extract PostalCode from LocationCode ─────────────────────────────
# LocationCode format for DE rows: DE~59821~Arnsberg~Hellefelder Str. 8
# PLZ = token between 1st and 2nd tilde (always 5 chars, but we extract by position)
# Only update rows where LocationCode matches DE~...~... AND PostalCode is currently NULL.

Write-Host "`n========================================" -ForegroundColor DarkGray
Write-Host "  Step 1: Extract PostalCode from LocationCode" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor DarkGray

$n = Exec @"
UPDATE WicLocations
SET PostalCode = SUBSTRING(
    LocationCode,
    CHARINDEX('~', LocationCode) + 1,
    CHARINDEX('~', LocationCode, CHARINDEX('~', LocationCode) + 1)
        - CHARINDEX('~', LocationCode) - 1
)
WHERE LocationCode LIKE 'DE~%~%'
  AND (PostalCode IS NULL OR PostalCode = '')
"@ "Extract PLZ from DE~PLZ~City~Addr pattern"

Write-Host "Expected: ~41 rows (all DE new-style LocationCodes)"

# ─── Step 2: Verify PostalCode extraction ─────────────────────────────────────

Query @"
SELECT
    COUNT(*) AS Total,
    SUM(CASE WHEN PostalCode IS NOT NULL AND PostalCode <> '' THEN 1 ELSE 0 END) AS WithPostalCode,
    SUM(CASE WHEN (PostalCode IS NULL OR PostalCode = '') AND Country = 'DE' THEN 1 ELSE 0 END) AS DEMissingPostalCode,
    SUM(CASE WHEN (PostalCode IS NULL OR PostalCode = '') AND Country = 'NL' THEN 1 ELSE 0 END) AS NLMissingPostalCode
FROM WicLocations WHERE IsActive = 1
"@ "PostalCode coverage after extraction"

Query @"
SELECT TOP 10 LocationCode, PostalCode, City, Country
FROM WicLocations
ORDER BY LocationCode
"@ "TOP 10 rows (spot-check extraction)"

# ─── Step 3: Re-run Bundesland UPDATE now that PostalCode is populated ─────────

Write-Host "`n========================================" -ForegroundColor DarkGray
Write-Host "  Step 3: Populate Bundesland by PostalCode" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor DarkGray

$plzMap = [ordered]@{
    "86150" = "Bayern"
    "96052" = "Bayern"
    "84051" = "Bayern"
    "97506" = "Bayern"
    "84036" = "Bayern"
    "80634" = "Bayern"
    "85276" = "Bayern"
    "93059" = "Bayern"
    "10589" = "Berlin"
    "12355" = "Berlin"
    "15517" = "Brandenburg"
    "14467" = "Brandenburg"
    "20537" = "Hamburg"
    "63263" = "Hessen"
    "17109" = "Mecklenburg-Vorpommern"
    "31860" = "Niedersachsen"
    "30459" = "Niedersachsen"
    "38350" = "Niedersachsen"
    "49074" = "Niedersachsen"
    "38226" = "Niedersachsen"
    "21683" = "Niedersachsen"
    "26935" = "Niedersachsen"
    "59821" = "Nordrhein-Westfalen"
    "44139" = "Nordrhein-Westfalen"
    "45131" = "Nordrhein-Westfalen"
    "45143" = "Nordrhein-Westfalen"
    "45476" = "Nordrhein-Westfalen"
    "48163" = "Nordrhein-Westfalen"
    "41460" = "Nordrhein-Westfalen"
    "45661" = "Nordrhein-Westfalen"
    "57072" = "Nordrhein-Westfalen"
    "46483" = "Nordrhein-Westfalen"
    "56648" = "Rheinland-Pfalz"
    "54294" = "Rheinland-Pfalz"
    "66121" = "Saarland"
    "04416" = "Sachsen"
    "06112" = "Sachsen-Anhalt"
    "25576" = "Schleswig-Holstein"
    "25451" = "Schleswig-Holstein"
    "24768" = "Schleswig-Holstein"
}

$totalUpdated = 0
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

foreach ($plz in $plzMap.Keys) {
    $bl  = $plzMap[$plz]
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = @"
UPDATE WicLocations
SET Bundesland = @bl
WHERE PostalCode = @plz
  AND (Bundesland IS NULL OR Bundesland <> @bl)
"@
    $cmd.Parameters.AddWithValue("@bl",  $bl)  | Out-Null
    $cmd.Parameters.AddWithValue("@plz", $plz) | Out-Null
    $n2 = $cmd.ExecuteNonQuery()
    if ($n2 -gt 0) {
        Write-Host "  $plz → $bl ($n2 row(s))" -ForegroundColor Green
        $totalUpdated += $n2
    }
}
$conn.Close()
Write-Host "PLZ-based Bundesland rows updated: $totalUpdated" -ForegroundColor $(if ($totalUpdated -gt 30) {"Green"} else {"Yellow"})

# LocationCode fallback for old-style codes (RENDSBURG, PFAFFENHOFEN — already set by PS1_5, idempotent)
Exec @"
UPDATE WicLocations SET Bundesland = 'Schleswig-Holstein'
WHERE LocationCode LIKE '%RENDSBURG%' AND (Bundesland IS NULL OR Bundesland <> 'Schleswig-Holstein')
"@ "LocationCode fallback: RENDSBURG → Schleswig-Holstein"

Exec @"
UPDATE WicLocations SET Bundesland = 'Bayern'
WHERE LocationCode LIKE '%PFAFFENHOFEN%' AND (Bundesland IS NULL OR Bundesland <> 'Bayern')
"@ "LocationCode fallback: PFAFFENHOFEN → Bayern"

# ─── Step 4: Verify — must show MissingBundesland = 0 ─────────────────────────

Write-Host "`n========================================" -ForegroundColor DarkGray
Write-Host "  Step 4: Final verification" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor DarkGray

Query @"
SELECT
    COUNT(*)  AS TotalDE,
    SUM(CASE WHEN Bundesland IS NOT NULL THEN 1 ELSE 0 END) AS WithBundesland,
    SUM(CASE WHEN Bundesland IS NULL THEN 1 ELSE 0 END)     AS MissingBundesland
FROM WicLocations
WHERE IsActive = 1 AND Country <> 'NL'
"@ "Bundesland coverage for DE locations (MUST be MissingBundesland = 0)"

Query @"
SELECT
    SUM(CASE WHEN Bundesland IS NULL AND Country = 'NL' THEN 1 ELSE 0 END) AS NL_NoBundesland_expected,
    SUM(CASE WHEN Bundesland IS NOT NULL AND Country = 'NL' THEN 1 ELSE 0 END) AS NL_HasBundesland_unexpected
FROM WicLocations WHERE IsActive = 1
"@ "NL locations: Bundesland should be NULL (no Bundesland concept)"

Query @"
SELECT LocationCode, PostalCode, City, Country, Bundesland
FROM WicLocations
WHERE IsActive = 1
ORDER BY Country, Bundesland, City
"@ "All active WicLocations — final spot-check"

Write-Host "`n========================================" -ForegroundColor DarkGray
if ($totalUpdated -gt 30) {
    Write-Host "  PS1_6 complete. If MissingBundesland = 0 above, proceed to PS1_1_Build.ps1" -ForegroundColor Green
} else {
    Write-Host "  WARNING: fewer than expected Bundesland rows updated. Check output above." -ForegroundColor Red
    Write-Host "  Look for: rows with Country='DE' but still NULL Bundesland." -ForegroundColor Red
    Write-Host "  Likely cause: unknown PLZ not in the map above — add it and re-run." -ForegroundColor Red
}
Write-Host "========================================" -ForegroundColor DarkGray
