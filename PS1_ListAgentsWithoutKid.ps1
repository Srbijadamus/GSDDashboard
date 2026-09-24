# PS1_ListAgentsWithoutKid.ps1 — lists the ACTIVE employees with NO KID at all
# (PrimaryKid and SecondaryKid both NULL/empty). These 63 agents can only log in
# via their EmployeeId (rule c) until a KID is collected for them.
# READ-ONLY. Optionally exports a CSV for KID collection:
#   powershell -File PS1_ListAgentsWithoutKid.ps1 -CsvOut .\agents_without_kid.csv
param([string]$CsvOut = "")
$ErrorActionPreference = 'Stop'
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"

$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandText = @"
SELECT EmployeeId, FullName, TeamLeadName, PrimaryRole, Engagement
FROM Employees
WHERE IsActive = 1
  AND (PrimaryKid   IS NULL OR PrimaryKid   = '')
  AND (SecondaryKid IS NULL OR SecondaryKid = '')
ORDER BY TeamLeadName, FullName
"@
$da = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
$dt = New-Object System.Data.DataTable
[void]$da.Fill($dt)
$cn.Close()

Write-Output ("Active employees with NO KID: " + $dt.Rows.Count)
$dt | Format-Table -AutoSize | Out-String -Width 300 | Write-Output

if ($CsvOut -ne "") {
  $dt | Export-Csv -NoTypeInformation -Encoding UTF8 $CsvOut
  Write-Output "CSV written: $CsvOut"
}
Write-Output "=== DONE ==="
