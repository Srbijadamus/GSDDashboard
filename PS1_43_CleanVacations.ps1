# PS1_43_CleanVacations.ps1 - Delete exact duplicate vacation record
# Keeps the lowest Id, deletes the higher duplicate
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

Write-Host "`n=== Preview: duplicate to be deleted ===" -ForegroundColor Yellow
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
SELECT v.Id, v.EmployeeId, e.FullName, v.FirstDay, v.LastDay
FROM Vacations v
LEFT JOIN Employees e ON e.EmployeeId = v.EmployeeId
WHERE v.EmployeeId = '9078602' AND v.FirstDay = '2026-02-11'
ORDER BY v.Id
"@
$reader = $cmd.ExecuteReader()
$rows = @()
while ($reader.Read()) {
    $rows += [PSCustomObject][ordered]@{
        Id        = $reader["Id"]
        EmployeeId = $reader["EmployeeId"]
        FullName  = $reader["FullName"]
        FirstDay  = $reader["FirstDay"]
        LastDay   = $reader["LastDay"]
    }
}
$reader.Close()
$rows | Format-Table -AutoSize

if ($rows.Count -ne 2) {
    Write-Host "Expected 2 rows, found $($rows.Count) - aborting." -ForegroundColor Red
    $conn.Close(); exit 1
}

$keepId   = ($rows | Sort-Object Id | Select-Object -First 1).Id
$deleteId = ($rows | Sort-Object Id | Select-Object -Last 1).Id
Write-Host "Keeping Id $keepId, deleting Id $deleteId" -ForegroundColor Cyan

$del = $conn.CreateCommand()
$del.CommandText = "DELETE FROM Vacations WHERE Id = $deleteId"
$affected = $del.ExecuteNonQuery()
Write-Host "Deleted $affected row(s)." -ForegroundColor Green

$conn.Close()
Write-Host "`nDone." -ForegroundColor Cyan
