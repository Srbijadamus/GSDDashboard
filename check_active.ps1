#Requires -Version 7.0
$conn = New-Object System.Data.SqlClient.SqlConnection "Server=localhost\SQLEXPRESS;Database=ShiftKioskDB;Integrated Security=true;TrustServerCertificate=true;"
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = @'
SELECT id, employee_id, first_name, last_name, team_leader, location, engagement
FROM agents
WHERE active = 1
ORDER BY last_name, first_name
'@

$adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
$ds = New-Object System.Data.DataSet
$adapter.Fill($ds) | Out-Null
$conn.Close()

$rows = $ds.Tables[0].Rows
Write-Host "Aktivnih: $($rows.Count)" -ForegroundColor Cyan
Write-Host ("{0,-6} {1,-14} {2,-22} {3,-25} {4,-25} {5}" -f "id","employee_id","first_name","last_name","team_leader","location")
Write-Host ("-"*110)
foreach ($r in $rows) {
    Write-Host ("{0,-6} {1,-14} {2,-22} {3,-25} {4,-25} {5}" -f $r.id, $r.employee_id, $r.first_name, $r.last_name, $r.team_leader, $r.location)
}
