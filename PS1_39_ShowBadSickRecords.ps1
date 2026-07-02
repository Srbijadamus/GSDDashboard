# PS1_39_ShowBadSickRecords.ps1 - Show bad SickLeaves records (read-only, no changes)
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

function Run-Query($sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
    $reader = $cmd.ExecuteReader()
    $rows = @()
    while ($reader.Read()) {
        $row = [ordered]@{}
        for ($i = 0; $i -lt $reader.FieldCount; $i++) { $row[$reader.GetName($i)] = $reader.GetValue($i) }
        $rows += [PSCustomObject]$row
    }
    $reader.Close()
    return $rows
}

Write-Host "`n=== 1. NON-SL records active today (LeaveType != 'SL') ===" -ForegroundColor Yellow
$nonSL = Run-Query @"
SELECT sl.Id, sl.EmployeeId, e.FullName, sl.FirstDay, sl.LastDay, sl.LeaveType, sl.SourceSheet
FROM SickLeaves sl
LEFT JOIN Employees e ON e.EmployeeId = sl.EmployeeId
WHERE sl.FirstDay <= CAST(GETDATE() AS DATE)
  AND sl.LastDay  >= CAST(GETDATE() AS DATE)
  AND sl.LeaveType <> 'SL'
ORDER BY sl.LeaveType, e.FullName
"@
$nonSL | Format-Table -AutoSize

Write-Host "`n=== 2. Records with span > 60 days (bad data) ===" -ForegroundColor Yellow
$longRec = Run-Query @"
SELECT sl.Id, sl.EmployeeId, e.FullName, sl.FirstDay, sl.LastDay, sl.LeaveType, sl.SourceSheet,
       DATEDIFF(day, sl.FirstDay, sl.LastDay) AS DaySpan
FROM SickLeaves sl
LEFT JOIN Employees e ON e.EmployeeId = sl.EmployeeId
WHERE DATEDIFF(day, sl.FirstDay, sl.LastDay) > 60
ORDER BY DaySpan DESC
"@
$longRec | Format-Table -AutoSize

Write-Host "`n=== 3. Overlapping SL records per employee (potential duplicates) ===" -ForegroundColor Yellow
$dupes = Run-Query @"
SELECT a.EmployeeId, e.FullName, a.Id AS Id_A, a.FirstDay AS FirstDay_A, a.LastDay AS LastDay_A,
       b.Id AS Id_B, b.FirstDay AS FirstDay_B, b.LastDay AS LastDay_B
FROM SickLeaves a
JOIN SickLeaves b ON a.EmployeeId = b.EmployeeId AND a.Id < b.Id
LEFT JOIN Employees e ON e.EmployeeId = a.EmployeeId
WHERE a.FirstDay <= b.LastDay AND a.LastDay >= b.FirstDay
  AND a.LeaveType = 'SL' AND b.LeaveType = 'SL'
ORDER BY a.EmployeeId
"@
$dupes | Format-Table -AutoSize

Write-Host "`n=== 4. Sebastian Hock (9074573) - all records ===" -ForegroundColor Yellow
$hock = Run-Query @"
SELECT sl.Id, sl.EmployeeId, e.FullName, sl.FirstDay, sl.LastDay, sl.LeaveType, sl.DurationDays, sl.SourceSheet
FROM SickLeaves sl
LEFT JOIN Employees e ON e.EmployeeId = sl.EmployeeId
WHERE sl.EmployeeId = '9074573'
"@
$hock | Format-Table -AutoSize

$conn.Close()
Write-Host "`nDone - NO changes made." -ForegroundColor Cyan
