# PS1_3_Geocoder.ps1 — Install geopy, run geocoder.py to backfill WicLocations.Coordinates.
# Run AFTER PS1_2_SchemaBuild.ps1 confirmed success.

Write-Host "=== Step 1: Ensure Python + pip available ===" -ForegroundColor Yellow
$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) {
    Write-Host "ERROR: Python not found in PATH. Install Python 3.x and re-run." -ForegroundColor Red
    exit 1
}
python --version

Write-Host "`n=== Step 2: Install geopy and pyodbc ===" -ForegroundColor Yellow
python -m pip install --quiet geopy pyodbc
if ($LASTEXITCODE -ne 0) { Write-Host "pip install failed" -ForegroundColor Red; exit 1 }

Write-Host "`n=== Step 3: Run geocoder ===" -ForegroundColor Yellow
python "C:\GSDDashboard\geocoder.py"

Write-Host "`n=== Step 4: Verify coordinates in DB ===" -ForegroundColor Yellow
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
SELECT
    COUNT(*) AS Total,
    SUM(CASE WHEN Coordinates IS NOT NULL THEN 1 ELSE 0 END) AS WithCoords,
    SUM(CASE WHEN Coordinates IS NULL THEN 1 ELSE 0 END) AS Missing
FROM WicLocations WHERE IsActive=1
"@
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Host "Total active: $($reader['Total'])  WithCoords: $($reader['WithCoords'])  Missing: $($reader['Missing'])" -ForegroundColor Green
}
$reader.Close()

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT TOP 5 LocationCode, City, Coordinates FROM WicLocations WHERE IsActive=1 AND Coordinates IS NOT NULL ORDER BY LocationCode"
$r2 = $cmd2.ExecuteReader()
$rows = @()
while ($r2.Read()) { $rows += [pscustomobject]@{ Code=$r2['LocationCode']; City=$r2['City']; Coords=$r2['Coordinates'] } }
$r2.Close(); $conn.Close()
$rows | Format-Table -AutoSize

Write-Host "`n=== Done ===" -ForegroundColor Green
