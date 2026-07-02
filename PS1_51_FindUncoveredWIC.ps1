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

# WIC main agents sick or on AL today
Run-Query "MAIN agents absent today (sick/AL) - creates coverage gap" @"
SELECT w.LocationCode, l.DisplayName, w.EmployeeName, s.ShiftType, sl.LeaveType
FROM WicAgentAssignments w
JOIN WicLocations l ON w.LocationCode = l.LocationCode OR w.LocationCode = l.LocationCodeLegacy
LEFT JOIN Employees e ON e.FullName = w.EmployeeName COLLATE SQL_Latin1_General_CP1_CI_AS
LEFT JOIN ShiftEntries s ON s.EmployeeId = e.EmployeeId AND s.ShiftDate = '2026-06-22'
LEFT JOIN SickLeave sl ON sl.EmployeeId = e.EmployeeId
    AND sl.FirstDay <= '2026-06-22' AND sl.LastDay >= '2026-06-22'
    AND sl.LeaveType IN ('SL','Self')
WHERE w.IsActive = 1 AND w.AssignmentType = 'MAIN'
  AND l.IsActive = 1
  AND (s.ShiftType IN ('AL','UL','SL','HALF_AL') OR sl.LeaveType IS NOT NULL)
ORDER BY l.DisplayName
"@

# All WIC locations with their main agent count
Run-Query "All active WIC locations and MAIN agent count" @"
SELECT l.LocationCode, l.DisplayName, l.MinAgentsRequired,
       COUNT(w.Id) AS MainAgents
FROM WicLocations l
LEFT JOIN WicAgentAssignments w ON (w.LocationCode = l.LocationCode OR w.LocationCode = l.LocationCodeLegacy)
    AND w.IsActive = 1 AND w.AssignmentType = 'MAIN'
WHERE l.IsActive = 1
GROUP BY l.LocationCode, l.DisplayName, l.MinAgentsRequired
ORDER BY l.DisplayName
"@

$conn.Close()
Write-Host "`nDone." -ForegroundColor Cyan
