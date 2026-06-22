$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=True;"

$sql = @"
DELETE FROM ShiftEntries
WHERE SourceSheet = 'EXCEL'
AND EXISTS (
    SELECT 1 FROM ShiftEntries s2
    WHERE s2.EmployeeId  = ShiftEntries.EmployeeId
    AND   s2.ShiftDate   = ShiftEntries.ShiftDate
    AND   s2.SourceSheet = 'GSD_DE'
)
"@

$conn = New-Object System.Data.SqlClient.SqlConnection $connStr
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = $sql
$cmd.CommandTimeout = 60

$deleted = $cmd.ExecuteNonQuery()

$conn.Close()

Write-Host "Deleted $deleted duplicate EXCEL rows."
