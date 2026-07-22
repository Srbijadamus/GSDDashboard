$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()

$cmd = New-Object System.Data.SqlClient.SqlCommand("SELECT COUNT(*) AS Total, SUM(CASE WHEN IsActive=1 THEN 1 ELSE 0 END) AS Active, SUM(CASE WHEN IsActive=0 THEN 1 ELSE 0 END) AS Inactive FROM Employees", $conn)
$r = $cmd.ExecuteReader(); $r.Read() | Out-Null
Write-Host ("Total: {0}  |  Active: {1}  |  Inactive: {2}" -f $r["Total"], $r["Active"], $r["Inactive"]) -ForegroundColor Cyan
$r.Close()

Write-Host ""
$cmd2 = New-Object System.Data.SqlClient.SqlCommand("SELECT ISNULL(PrimaryRole,'(none)') AS PrimaryRole, COUNT(*) AS Cnt FROM Employees WHERE IsActive=1 GROUP BY PrimaryRole ORDER BY Cnt DESC", $conn)
$r2 = $cmd2.ExecuteReader()
Write-Host "Active by role:" -ForegroundColor Yellow
while ($r2.Read()) { Write-Host ("  {0,-30} {1}" -f $r2["PrimaryRole"], $r2["Cnt"]) }
$r2.Close()
$conn.Close()
