# PS1_38_SickLeave.ps1 - Sick Leave week of 2026-06-22
# Project: C:\GSDDashboard
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$inserted = 0; $skipped = 0

$data = @(
    @("9085123","2026-06-19","2026-06-26","SL"),
    @("9074557","2026-06-19","2026-06-26","SL"),
    @("9074563","2026-06-22","2026-06-26","SL"),
    @("9074373","2026-06-22","2026-06-24","SL"),
    @("9129428","2026-06-22","2026-06-22","SL"),
    @("9122675","2026-06-22","2026-06-26","SL"),
    @("9119463","2026-06-22","2026-06-26","SL"),
    @("4451020","2026-06-22","2026-06-22","SL"),
    @("9126882","2026-06-22","2026-06-26","SL"),
    @("9107616","2026-06-22","2026-06-26","SL"),
    @("9133995","2026-06-22","2026-06-26","SL")
)

foreach ($row in $data) {
    $empId = $row[0]; $firstDay = $row[1]; $lastDay = $row[2]; $type = $row[3]
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "IF EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId='$empId' AND FirstDay='$firstDay') UPDATE SickLeaves SET LastDay='$lastDay', LeaveType='$type' WHERE EmployeeId='$empId' AND FirstDay='$firstDay' ELSE INSERT INTO SickLeaves (EmployeeId, FirstDay, LastDay, LeaveType) VALUES ('$empId','$firstDay','$lastDay','$type')"
    try {
        $r = $cmd.ExecuteNonQuery()
        if ($r -gt 0) { $inserted++; Write-Host "  OK: $empId $firstDay - $lastDay" -ForegroundColor Green }
    } catch {
        Write-Host "  ERROR: $empId - $($_.Exception.Message)" -ForegroundColor Red
    }
}

$conn.Close()
Write-Host ""
Write-Host "Done. Processed: $inserted" -ForegroundColor Cyan
