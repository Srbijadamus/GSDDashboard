# PS1_22_FixOpeningHours.ps1
# Migrates WicOpeningHours.LocationCode from old-style (DE_xxx) to new-style (DE~xxx~...).
# Step 1: Backfill WicLocations.LocationCodeLegacy via PostalCode / DisplayName / City.
# Step 2: Update WicOpeningHours.LocationCode using the populated LocationCodeLegacy.
# Step 3: Verify expected row counts.
# No rebuild required - pure data fix.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function Exec($sql, $label) {
    Write-Host "`n>>> $label" -ForegroundColor Cyan
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        $cmd.CommandTimeout = 60
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
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        $cmd.CommandTimeout = 60
        $r = $cmd.ExecuteReader()
        $colNames = @()
        for ($i = 0; $i -lt $r.FieldCount; $i++) { $colNames += $r.GetName($i) }
        Write-Host ($colNames -join " | ") -ForegroundColor DarkGray
        Write-Host ("-" * 60) -ForegroundColor DarkGray
        $rows = @()
        while ($r.Read()) {
            $vals = @()
            $row = [ordered]@{}
            for ($i = 0; $i -lt $r.FieldCount; $i++) {
                $v = $r.GetValue($i)
                $s = if ($v -is [DBNull]) { "NULL" } else { "$v" }
                $vals += $s
                $row[$r.GetName($i)] = $s
            }
            Write-Host ($vals -join " | ")
            $rows += [pscustomobject]$row
        }
        $r.Close()
        $conn.Close()
        if ($rows.Count -eq 0) { Write-Host "(no rows)" -ForegroundColor Yellow }
        else { Write-Host "($($rows.Count) row(s))" -ForegroundColor DarkGray }
        return $rows
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
        return @()
    }
}

Write-Host "==================================================" -ForegroundColor Yellow
Write-Host " PS1_22 - Fix WicOpeningHours LocationCode"        -ForegroundColor Yellow
Write-Host "==================================================" -ForegroundColor Yellow

# ------------------------------------------------------------------------------
# STEP 1a - Backfill LocationCodeLegacy for 36 DE locations via PostalCode
# ------------------------------------------------------------------------------

$pairs = @(
    "('DE_Arnsberg','59821')",
    "('DE_Augsburg','86150')",
    "('DE_Bamberg','96052')",
    "('DE_Berlin_Gauss','10589')",
    "('DE_Berlin_Kopenick','12355')",
    "('DE_Brokdorf','25576')",
    "('DE_Dortmund','44139')",
    "('DE_Emmerthal','31860')",
    "('DE_Essen_BP1','45131')",
    "('DE_Essen_TK1','45143')",
    "('DE_Essenbach','84051')",
    "('DE_Furstenwalde','15517')",
    "('DE_Grafenrheinfeld','97506')",
    "('DE_Halle','06112')",
    "('DE_Hamburg','20537')",
    "('DE_Hannover','30459')",
    "('DE_Helmstedt','38350')",
    "('DE_Landshut','84036')",
    "('DE_Markkleeberg','04416')",
    "('DE_Munchen','80634')",
    "('DE_Mulheim','45476')",
    "('DE_Munster','48163')",
    "('DE_NeuIsenburg','63263')",
    "('DE_Neuss','41460')",
    "('DE_Osnabruck','49074')",
    "('DE_Potsdam','14467')",
    "('DE_Quickborn','25451')",
    "('DE_Recklinghausen','45661')",
    "('DE_Saarbrucken','66121')",
    "('DE_Saffig','56648')",
    "('DE_Salzgitter','38226')",
    "('DE_Siegen','57072')",
    "('DE_Stade','21683')",
    "('DE_Stadland','26935')",
    "('DE_Trier','54294')",
    "('DE_Wesel','46483')"
)

$valuesClause = $pairs -join ","

$sql1a = "UPDATE wl SET wl.LocationCodeLegacy = m.OldCode " +
         "FROM WicLocations wl " +
         "INNER JOIN (VALUES " + $valuesClause + ") AS m(OldCode, PostalCode) " +
         "ON wl.PostalCode = m.PostalCode AND wl.Country = 'DE' " +
         "WHERE wl.LocationCodeLegacy IS NULL"

Exec $sql1a "Step 1a: Backfill LocationCodeLegacy for 36 DE locations (PostalCode match)"

# ------------------------------------------------------------------------------
# STEP 1b - Demmin: two locations share PLZ 17109 - disambiguate by DisplayName
# ------------------------------------------------------------------------------

$sql1b = "UPDATE WicLocations SET LocationCodeLegacy = 'DE_Demmin_Hanse' " +
         "WHERE DisplayName LIKE '%Hanse%' AND LocationCodeLegacy IS NULL"

$sql1c = "UPDATE WicLocations SET LocationCodeLegacy = 'DE_Demmin_Wold' " +
         "WHERE DisplayName LIKE '%Wolde%' AND LocationCodeLegacy IS NULL"

Exec $sql1b "Step 1b: Demmin (Am Hanseufer)"
Exec $sql1c "Step 1c: Demmin (Woldeforster)"

# ------------------------------------------------------------------------------
# STEP 1d - Netherlands - no PostalCode, match by City
# ------------------------------------------------------------------------------

$sql1d = "UPDATE WicLocations SET LocationCodeLegacy = 'NL_Denbosch' " +
         "WHERE City = 's-Hertogenbosch' AND LocationCodeLegacy IS NULL"

$sql1e = "UPDATE WicLocations SET LocationCodeLegacy = 'NL_Zwolle' " +
         "WHERE City = 'Zwolle' AND LocationCodeLegacy IS NULL"

Exec $sql1d "Step 1d: NL - s-Hertogenbosch"
Exec $sql1e "Step 1e: NL - Zwolle"

# ------------------------------------------------------------------------------
# STEP 2 - Migrate WicOpeningHours.LocationCode to new-style via LocationCodeLegacy
# ------------------------------------------------------------------------------

$sql2 = "UPDATE woh SET woh.LocationCode = wl.LocationCode " +
        "FROM WicOpeningHours woh " +
        "INNER JOIN WicLocations wl ON wl.LocationCodeLegacy = woh.LocationCode " +
        "WHERE woh.LocationCode NOT LIKE 'DE~%' AND woh.LocationCode NOT LIKE 'NL~%'"

Exec $sql2 "Step 2: Migrate WicOpeningHours.LocationCode to new-style codes"

# ------------------------------------------------------------------------------
# STEP 3 - Verify
# ------------------------------------------------------------------------------

$sql3a = "SELECT " +
         "SUM(CASE WHEN LocationCode LIKE 'DE~%' OR LocationCode LIKE 'NL~%' THEN 1 ELSE 0 END) AS NewStyle, " +
         "SUM(CASE WHEN LocationCode LIKE 'DE[_]%' OR LocationCode LIKE 'NL[_]%' THEN 1 ELSE 0 END) AS OldStyle " +
         "FROM WicOpeningHours"

$sql3b = "SELECT COUNT(*) AS LocationsWithLegacyCode FROM WicLocations WHERE LocationCodeLegacy IS NOT NULL"

$r3a = Query $sql3a "Step 3a: Verify WicOpeningHours (expect NewStyle=306, OldStyle=0)"
$r3b = Query $sql3b "Step 3b: Verify WicLocations.LocationCodeLegacy (expect 42)"

Write-Host ""
if ($r3a -and $r3a[0].NewStyle -eq 306 -and $r3a[0].OldStyle -eq 0) {
    Write-Host "PASS WicOpeningHours: NewStyle=306, OldStyle=0" -ForegroundColor Green
} else {
    Write-Host "WARN WicOpeningHours: check output above - expected NewStyle=306 OldStyle=0" -ForegroundColor Yellow
}

if ($r3b -and $r3b[0].LocationsWithLegacyCode -eq 42) {
    Write-Host "PASS WicLocations: LocationsWithLegacyCode=42" -ForegroundColor Green
} else {
    Write-Host "WARN WicLocations: check output above - expected 42" -ForegroundColor Yellow
}

Write-Host "`n==================================================" -ForegroundColor Yellow
Write-Host " Migration complete. No rebuild required."          -ForegroundColor Green
Write-Host " Reload the Overview page to confirm heatmap."     -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Yellow
