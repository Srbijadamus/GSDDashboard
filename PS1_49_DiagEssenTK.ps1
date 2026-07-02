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

Run-Query "Essen-TK location" @"
SELECT LocationCode, LocationCodeLegacy, DisplayName, Coordinates, IsActive
FROM WicLocations
WHERE DisplayName LIKE '%Essen%' OR DisplayName LIKE '%TK%'
ORDER BY DisplayName
"@

Run-Query "All WIC locations (for reference)" @"
SELECT LocationCode, LocationCodeLegacy, DisplayName, Coordinates, IsActive
FROM WicLocations WHERE IsActive=1
ORDER BY DisplayName
"@

Run-Query "SSP/Voice agents active today with shifts on 2026-06-22" @"
SELECT e.EmployeeId, e.FullName, e.PrimaryRole, e.Bundesland,
       s.ShiftType, s.ShiftStart, s.ShiftEnd
FROM Employees e
LEFT JOIN ShiftEntries s ON e.EmployeeId = s.EmployeeId AND s.ShiftDate = '2026-06-22'
WHERE e.IsActive = 1
  AND e.PrimaryRole IN ('SSP','Voice','VWIC')
ORDER BY e.PrimaryRole, e.FullName
"@

$conn.Close()
Write-Host "`nDone - NO changes made." -ForegroundColor Cyan
