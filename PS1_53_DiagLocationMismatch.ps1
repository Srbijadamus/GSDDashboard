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
    return $rows
}

Run-Query "DIAGNOSTIC 1 - SupportLocation values with NO matching WicLocations.DisplayName" @"
SELECT DISTINCT w.SupportLocation, COUNT(*) AS EntryCount
FROM WicShiftEntries w
WHERE w.ShiftDate >= '2026-06-22'
AND NOT EXISTS (
    SELECT 1 FROM WicLocations l
    WHERE l.DisplayName = w.SupportLocation
)
GROUP BY w.SupportLocation
ORDER BY w.SupportLocation
"@

Run-Query "DIAGNOSTIC 1b - All distinct WicLocations DisplayNames (for comparison)" @"
SELECT DisplayName, LocationCode, IsActive
FROM WicLocations
ORDER BY DisplayName
"@

Run-Query "DIAGNOSTIC 2 - IsOnSite=1 agents with active SickLeave on same date (2026-06-22)" @"
SELECT w.EmployeeId, e.FullName, w.SupportLocation, w.ShiftDate,
       s.LeaveType, s.FirstDay, s.LastDay
FROM WicShiftEntries w
JOIN Employees e ON w.EmployeeId = e.EmployeeId
JOIN SickLeave s ON w.EmployeeId = s.EmployeeId
    AND w.ShiftDate >= s.FirstDay AND w.ShiftDate <= s.LastDay
WHERE w.IsOnSite = 1 AND w.ShiftDate = '2026-06-22'
ORDER BY w.SupportLocation
"@

Run-Query "DIAGNOSTIC 2b - IsOnSite=1 agents with ShiftEntry AL/UL/SL on same date (2026-06-22)" @"
SELECT w.EmployeeId, e.FullName, w.SupportLocation, w.ShiftDate,
       se.ShiftType
FROM WicShiftEntries w
JOIN Employees e ON w.EmployeeId = e.EmployeeId
JOIN ShiftEntries se ON w.EmployeeId = se.EmployeeId AND se.ShiftDate = w.ShiftDate
WHERE w.IsOnSite = 1 AND w.ShiftDate = '2026-06-22'
  AND se.ShiftType IN ('AL','UL','SL','HALF_AL','PH','LPH')
ORDER BY w.SupportLocation
"@

$conn.Close()
Write-Host "`nDone - NO changes made." -ForegroundColor Cyan
