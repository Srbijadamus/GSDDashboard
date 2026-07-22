#Requires -Version 7.0

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
$conn = [System.Data.SqlClient.SqlConnection]::new($cs)
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Employees SET IsActive = 0 WHERE EonEmail = 'Kavin.Pathmanathan.external@eon.com'"
$rows = $cmd.ExecuteNonQuery()
Write-Host "Affected rows: $rows"

$cmd.CommandText = "SELECT COUNT(*) FROM Employees WHERE IsActive = 1 AND EonEmail IS NOT NULL"
$count = $cmd.ExecuteScalar()
Write-Host "Ukupno aktivnih sa EON emailom: $count"

$conn.Close()
