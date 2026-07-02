$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

function Run-Query($label, $sql) {
    Write-Host "`n=== $label ===" -ForegroundColor Yellow
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
    $reader = $cmd.ExecuteReader()
    $rows = @()
    while ($reader.Read()) {
        $row = [ordered]@{}
        for ($i = 0; $i -lt $reader.FieldCount; $i++) { $row[$reader.GetName($i)] = $reader.GetValue($i) }
        $rows += [PSCustomObject]$row
    }
    $reader.Close()
    if ($rows.Count -eq 0) { Write-Host "  (none)" -ForegroundColor Green } else { $rows | Format-Table -AutoSize }
}

Run-Query "1. WicLocations coordinate coverage" @"
SELECT COUNT(*) AS Total,
       SUM(CASE WHEN Coordinates IS NOT NULL AND Coordinates != '' THEN 1 ELSE 0 END) AS WithCoords,
       SUM(CASE WHEN Coordinates IS NULL OR Coordinates = '' THEN 1 ELSE 0 END) AS MissingCoords
FROM WicLocations
"@

Run-Query "2. WIC agents + assigned location coordinates (TOP 20)" @"
SELECT TOP 20 e.EmployeeId, e.FullName, e.PrimaryRole,
       wa.LocationCode, w.LocationCode AS WicLocCode, w.Coordinates
FROM Employees e
LEFT JOIN WicAgentAssignments wa ON e.EmployeeId = wa.EmployeeId AND wa.IsActive = 1
LEFT JOIN WicLocations w ON wa.LocationCode = w.LocationCodeLegacy OR wa.LocationCode = w.LocationCode
WHERE e.PrimaryRole = 'WIC' AND e.IsActive = 1
ORDER BY e.FullName
"@

Run-Query "3. Active employees by PrimaryRole" @"
SELECT PrimaryRole, COUNT(*) AS Count
FROM Employees WHERE IsActive = 1
GROUP BY PrimaryRole ORDER BY PrimaryRole
"@

Run-Query "4. Base-category summary" @"
SELECT
  SUM(CASE WHEN PrimaryRole IN ('SSP','Voice','VWIC','Chat','Dispatcher','SME','Booking Tool') THEN 1 ELSE 0 END) AS HQ_Based,
  SUM(CASE WHEN PrimaryRole = 'WIC' THEN 1 ELSE 0 END) AS WIC_LocationBased,
  SUM(CASE WHEN PrimaryRole = '2nd Level' THEN 1 ELSE 0 END) AS SecondLevel_Excluded,
  SUM(CASE WHEN PrimaryRole NOT IN ('SSP','Voice','VWIC','Chat','Dispatcher','SME','Booking Tool','WIC','2nd Level') THEN 1 ELSE 0 END) AS Other
FROM Employees WHERE IsActive = 1
"@

Run-Query "5. WicLocations missing coordinates" @"
SELECT LocationCode, LocationCodeLegacy, DisplayName, Coordinates
FROM WicLocations
WHERE Coordinates IS NULL OR Coordinates = ''
ORDER BY LocationCode
"@

$conn.Close()
Write-Host "`nDone - NO changes made." -ForegroundColor Cyan
