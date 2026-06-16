# PS1_5_BundeslandMigration.ps1
# Idempotent: adds Bundesland column to WicLocations if missing, then populates it.
# Also adds Coordinates and MinAgentsRequired if they were not added by PS1_2_SchemaBuild.ps1.
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
        Write-Host "OK ($rows rows affected)" -ForegroundColor Green
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
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

# ─── Step 1: Add columns if missing (idempotent) ──────────────────────────────

Exec @"
IF COL_LENGTH('WicLocations','Coordinates') IS NULL
    ALTER TABLE WicLocations ADD Coordinates NVARCHAR(50) NULL;
"@ "Add Coordinates column (no-op if exists)"

Exec @"
IF COL_LENGTH('WicLocations','MinAgentsRequired') IS NULL
    ALTER TABLE WicLocations ADD MinAgentsRequired INT NULL;
"@ "Add MinAgentsRequired column (no-op if exists)"

Exec @"
IF COL_LENGTH('WicLocations','Bundesland') IS NULL
    ALTER TABLE WicLocations ADD Bundesland NVARCHAR(50) NULL;
"@ "Add Bundesland column (no-op if exists)"

# ─── Step 2: Populate Bundesland via PLZ ──────────────────────────────────────
# All 40 seeded DE locations, keyed by PostalCode or LocationCode.
# NL locations (Country='NL') are left NULL.

# PLZ-based updates (most reliable; covers all DE schema.sql locations)
$plzMap = @{
    "86150" = "Bayern"                    # Augsburg
    "96052" = "Bayern"                    # Bamberg
    "84051" = "Bayern"                    # Essenbach
    "97506" = "Bayern"                    # Grafenrheinfeld
    "84036" = "Bayern"                    # Landshut
    "80634" = "Bayern"                    # Muenchen Arnulfstr
    "85276" = "Bayern"                    # Pfaffenhofen
    "93059" = "Bayern"                    # Regensburg
    "10589" = "Berlin"                    # Berlin Gaussstr
    "12355" = "Berlin"                    # Berlin Koepenicker
    "15517" = "Brandenburg"               # Fuerstenwalde
    "14467" = "Brandenburg"               # Potsdam
    "20537" = "Hamburg"                   # Hamburg
    "63263" = "Hessen"                    # Neu-Isenburg
    "17109" = "Mecklenburg-Vorpommern"    # Demmin (both WICs share PLZ)
    "31860" = "Niedersachsen"             # Emmerthal
    "30459" = "Niedersachsen"             # Hannover
    "38350" = "Niedersachsen"             # Helmstedt
    "49074" = "Niedersachsen"             # Osnabrueck
    "38226" = "Niedersachsen"             # Salzgitter
    "21683" = "Niedersachsen"             # Stade
    "26935" = "Niedersachsen"             # Stadland
    "59821" = "Nordrhein-Westfalen"       # Arnsberg
    "44139" = "Nordrhein-Westfalen"       # Dortmund
    "45131" = "Nordrhein-Westfalen"       # Essen Bruesseler
    "45143" = "Nordrhein-Westfalen"       # Essen ThyssenKrupp
    "45476" = "Nordrhein-Westfalen"       # Muelheim
    "48163" = "Nordrhein-Westfalen"       # Muenster
    "41460" = "Nordrhein-Westfalen"       # Neuss
    "45661" = "Nordrhein-Westfalen"       # Recklinghausen
    "57072" = "Nordrhein-Westfalen"       # Siegen
    "46483" = "Nordrhein-Westfalen"       # Wesel
    "56648" = "Rheinland-Pfalz"           # Saffig
    "54294" = "Rheinland-Pfalz"           # Trier
    "66121" = "Saarland"                  # Saarbruecken
    "04416" = "Sachsen"                   # Markkleeberg
    "06112" = "Sachsen-Anhalt"            # Halle Saale
    "25576" = "Schleswig-Holstein"        # Brokdorf
    "25451" = "Schleswig-Holstein"        # Quickborn
    "24768" = "Schleswig-Holstein"        # Rendsburg
}

Write-Host "`n>>> Updating Bundesland by PostalCode ($($plzMap.Count) entries)" -ForegroundColor Cyan
$totalUpdated = 0
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

foreach ($plz in $plzMap.Keys) {
    $bl  = $plzMap[$plz]
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = @"
UPDATE WicLocations
SET Bundesland = @bl
WHERE PostalCode = @plz AND (Bundesland IS NULL OR Bundesland <> @bl)
"@
    $cmd.Parameters.AddWithValue("@bl",  $bl)  | Out-Null
    $cmd.Parameters.AddWithValue("@plz", $plz) | Out-Null
    $n = $cmd.ExecuteNonQuery()
    if ($n -gt 0) {
        Write-Host "  $plz → $bl ($n row(s))" -ForegroundColor Green
        $totalUpdated += $n
    }
}
$conn.Close()
Write-Host "Total rows updated: $totalUpdated" -ForegroundColor Green

# LocationCode-based fallback for old-style codes (no PLZ match)
$codeMap = @{
    "RENDSBURG"    = "Schleswig-Holstein"
    "PFAFFENHOFEN" = "Bayern"
}
foreach ($code in $codeMap.Keys) {
    $bl = $codeMap[$code]
    Exec @"
UPDATE WicLocations
SET Bundesland = '$bl'
WHERE LocationCode LIKE '%$code%' AND (Bundesland IS NULL OR Bundesland <> '$bl')
"@ "LocationCode fallback: $code → $bl"
}

# ─── Step 3: Verify ────────────────────────────────────────────────────────────

Query @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WicLocations'
  AND COLUMN_NAME IN ('Bundesland','Coordinates','MinAgentsRequired')
ORDER BY COLUMN_NAME
"@ "INFORMATION_SCHEMA: confirm 3 new columns exist"

Query @"
SELECT
    COUNT(*) AS TotalDE,
    SUM(CASE WHEN Bundesland IS NOT NULL THEN 1 ELSE 0 END) AS WithBundesland,
    SUM(CASE WHEN Bundesland IS NULL AND Country <> 'NL' THEN 1 ELSE 0 END) AS MissingBundesland
FROM WicLocations
WHERE IsActive = 1 AND Country <> 'NL'
"@ "Bundesland coverage for DE locations (expected: 0 missing)"

Query @"
SELECT LocationCode, PostalCode, City, Country, Bundesland
FROM WicLocations
WHERE IsActive = 1
ORDER BY Country, Bundesland, City
"@ "All active WicLocations with Bundesland (spot-check)"

Write-Host "`n=== PS1_5 complete. Next: run PS1_3_Geocoder.ps1 to backfill Coordinates. ===" -ForegroundColor Green
