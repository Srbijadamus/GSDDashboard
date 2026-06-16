# PS1_7_FixMissing2.ps1
# Adds the two PLZs missing from PS1_6's lookup map:
#   10179 → Berlin  (DE~10179~Berlin~Brückenstrasse 6)
#   93049 → Bayern  (DE~93049~Regensburg~Lilienthalstraße 7)
# Then verifies MissingBundesland = 0 for all DE locations.
# Run from PowerShell 7.6. No git commands.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

Write-Host "`n>>> Updating 2 missing Bundesland rows" -ForegroundColor Cyan

foreach ($row in @(
    @{ plz = "10179"; bl = "Berlin" },
    @{ plz = "93049"; bl = "Bayern" }
)) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "UPDATE WicLocations SET Bundesland = @bl WHERE PostalCode = @plz AND (Bundesland IS NULL OR Bundesland <> @bl)"
    $cmd.Parameters.AddWithValue("@bl",  $row.bl)  | Out-Null
    $cmd.Parameters.AddWithValue("@plz", $row.plz) | Out-Null
    $n = $cmd.ExecuteNonQuery()
    Write-Host "  $($row.plz) → $($row.bl): $n row(s)" -ForegroundColor $(if ($n -gt 0) { "Green" } else { "Yellow" })
}

Write-Host "`n>>> Verification: MissingBundesland must = 0" -ForegroundColor Cyan

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = @"
SELECT
    COUNT(*) AS TotalDE,
    SUM(CASE WHEN Bundesland IS NOT NULL THEN 1 ELSE 0 END) AS WithBundesland,
    SUM(CASE WHEN Bundesland IS NULL     THEN 1 ELSE 0 END) AS MissingBundesland
FROM WicLocations
WHERE IsActive = 1 AND Country <> 'NL'
"@
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    $total   = $r["TotalDE"]
    $with    = $r["WithBundesland"]
    $missing = $r["MissingBundesland"]
    $color   = if ([int]$missing -eq 0) { "Green" } else { "Red" }
    Write-Host "  TotalDE=$total  WithBundesland=$with  MissingBundesland=$missing" -ForegroundColor $color
    if ([int]$missing -gt 0) {
        Write-Host "  ACTION NEEDED: run the query below to see remaining NULL rows:" -ForegroundColor Red
        Write-Host "  SELECT LocationCode, PostalCode, City FROM WicLocations WHERE IsActive=1 AND Country<>'NL' AND Bundesland IS NULL" -ForegroundColor Yellow
    }
}
$r.Close()

Write-Host "`n>>> All active locations (final spot-check)" -ForegroundColor Cyan
$cmd3 = $conn.CreateCommand()
$cmd3.CommandText = "SELECT LocationCode, PostalCode, City, Country, Bundesland FROM WicLocations WHERE IsActive=1 ORDER BY Country, Bundesland, City"
$r3 = $cmd3.ExecuteReader()
$rows = @()
while ($r3.Read()) {
    $rows += [pscustomobject]@{
        LocationCode = $r3["LocationCode"]
        PostalCode   = if ($r3["PostalCode"] -is [DBNull]) { "NULL" } else { $r3["PostalCode"] }
        City         = if ($r3["City"]       -is [DBNull]) { "NULL" } else { $r3["City"] }
        Country      = $r3["Country"]
        Bundesland   = if ($r3["Bundesland"] -is [DBNull]) { "NULL" } else { $r3["Bundesland"] }
    }
}
$r3.Close(); $conn.Close()
$rows | Format-Table -AutoSize

Write-Host "`n>>> Done. If MissingBundesland=0 above, proceed to PS1_1_Build.ps1" -ForegroundColor Green
