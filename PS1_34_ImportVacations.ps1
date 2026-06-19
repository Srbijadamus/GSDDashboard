$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true"

$rows = @(
    @("9074516","2026-03-30","2026-04-02",""),
    @("9074579","2026-01-02","2026-01-02","1 day left from 2025"),
    @("9074531","2026-01-02","2026-01-02","4 days from 2025"),
    @("9074369","2026-04-07","2026-04-08",""),
    @("9074376","2026-01-02","2026-01-02","2 days from 2025"),
    @("9074376","2026-02-05","2026-02-05",""),
    @("9074376","2026-08-03","2026-08-14",""),
    @("9074579","2026-01-16","2026-01-16",""),
    @("9074579","2026-02-05","2026-02-10",""),
    @("9074531","2026-01-16","2026-01-19",""),
    @("9074531","2026-03-18","2026-03-20",""),
    @("9074579","2026-03-18","2026-03-20",""),
    @("9074369","2026-04-09","2026-04-10",""),
    @("9074579","2026-04-10","2026-04-13",""),
    @("9074531","2026-04-10","2026-04-13",""),
    @("9074475","2026-05-04","2026-05-08",""),
    @("9074475","2026-07-06","2026-07-10",""),
    @("9074376","2026-05-15","2026-05-15",""),
    @("9074376","2026-11-04","2026-11-04",""),
    @("9074376","2026-12-21","2026-12-31",""),
    @("9074376","2026-06-05","2026-06-05","")
)

$sql = "IF NOT EXISTS (SELECT 1 FROM Vacations WHERE EmployeeId = @eid AND FirstDay = @fd AND LastDay = @ld) " +
       "INSERT INTO Vacations (EmployeeId, FirstDay, LastDay, Comments) VALUES (@eid, @fd, @ld, @cmt)"

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

$inserted = 0
$skipped  = 0

foreach ($r in $rows) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.Parameters.AddWithValue("@eid", $r[0]) | Out-Null
    $cmd.Parameters.AddWithValue("@fd",  $r[1]) | Out-Null
    $cmd.Parameters.AddWithValue("@ld",  $r[2]) | Out-Null
    $cmd.Parameters.AddWithValue("@cmt", $r[3]) | Out-Null
    $affected = $cmd.ExecuteNonQuery()
    if ($affected -gt 0) {
        Write-Host "  inserted  $($r[0])  $($r[1]) -> $($r[2])"
        $inserted++
    } else {
        Write-Host "  skipped   $($r[0])  $($r[1]) -> $($r[2])  (already exists)"
        $skipped++
    }
    $cmd.Dispose()
}

$conn.Close()

Write-Host ""
Write-Host "Done. Inserted: $inserted  Skipped: $skipped  Total: $($rows.Count)"
