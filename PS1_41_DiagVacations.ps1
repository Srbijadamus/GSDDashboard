# PS1_41_DiagVacations.ps1 - Vacation diagnostic (read-only)
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

Run-Query "Orphaned records (EmployeeId not in Employees)" @"
SELECT v.EmployeeId, e.FullName, COUNT(*) AS Periods
FROM Vacations v
LEFT JOIN Employees e ON v.EmployeeId = e.EmployeeId
WHERE e.EmployeeId IS NULL
GROUP BY v.EmployeeId, e.FullName
ORDER BY v.EmployeeId
"@

Run-Query "Exact duplicates (same EmployeeId+FirstDay+LastDay)" @"
SELECT EmployeeId, FirstDay, LastDay, COUNT(*) AS cnt
FROM Vacations
GROUP BY EmployeeId, FirstDay, LastDay
HAVING COUNT(*) > 1
ORDER BY cnt DESC
"@

$conn.Close()
Write-Host "`nDone - NO changes made." -ForegroundColor Cyan
