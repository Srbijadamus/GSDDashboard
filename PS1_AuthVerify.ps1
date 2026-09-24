# PS1_AuthVerify.ps1 - READ-ONLY verification for KID login + RBAC task (Phase 1 follow-up)
# Runs SELECT queries only. No writes. Approved in odgovor.md section 2.
$ErrorActionPreference = 'Stop'
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"

function Run-Q($label, $sql) {
  $cn = New-Object System.Data.SqlClient.SqlConnection $cs
  $cn.Open()
  $cmd = $cn.CreateCommand()
  $cmd.CommandText = $sql
  $da = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
  $dt = New-Object System.Data.DataTable
  [void]$da.Fill($dt)
  $cn.Close()
  Write-Output ("=== " + $label + " ===")
  $dt | Format-Table -AutoSize | Out-String -Width 300 | Write-Output
}

Run-Q "1 Employees columns" "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH AS MaxLen, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' ORDER BY ORDINAL_POSITION"

Run-Q "2 Auth-like tables" "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%User%' OR TABLE_NAME LIKE '%Role%' OR TABLE_NAME LIKE '%Auth%' OR TABLE_NAME LIKE '%Login%'"

Run-Q "3 KID population" "SELECT COUNT(*) AS Total, SUM(CASE WHEN IsActive=1 THEN 1 ELSE 0 END) AS Active, SUM(CASE WHEN PrimaryKid IS NULL OR PrimaryKid='' THEN 1 ELSE 0 END) AS NullPrimaryKid, SUM(CASE WHEN IsActive=1 AND (PrimaryKid IS NULL OR PrimaryKid='') THEN 1 ELSE 0 END) AS ActiveNullPrimaryKid, SUM(CASE WHEN SecondaryKid IS NULL OR SecondaryKid='' THEN 1 ELSE 0 END) AS NullSecondaryKid FROM Employees"

Run-Q "4 Duplicate PrimaryKid" "SELECT PrimaryKid, COUNT(*) AS C FROM Employees WHERE PrimaryKid IS NOT NULL AND PrimaryKid<>'' GROUP BY PrimaryKid HAVING COUNT(*)>1"

Run-Q "4b Duplicate KID across Primary and Secondary" "SELECT Kid, COUNT(*) AS C FROM (SELECT PrimaryKid AS Kid FROM Employees WHERE PrimaryKid IS NOT NULL AND PrimaryKid<>'' UNION ALL SELECT SecondaryKid AS Kid FROM Employees WHERE SecondaryKid IS NOT NULL AND SecondaryKid<>'') x GROUP BY Kid HAVING COUNT(*)>1"

Run-Q "5 Seed KIDs" "SELECT EmployeeId, FullName, PrimaryKid, SecondaryKid, IsActive, PrimaryRole, TeamLeadName FROM Employees WHERE PrimaryKid IN ('S60028','Y6525','S69307') OR SecondaryKid IN ('S60028','Y6525','S69307')"

Run-Q "6 Team leads to KIDs" "SELECT DISTINCT tl.TeamLeadName, e.EmployeeId AS LeadEmployeeId, e.PrimaryKid AS LeadKid, e.IsActive AS LeadActive FROM Employees tl LEFT JOIN Employees e ON e.FullName = tl.TeamLeadName WHERE tl.IsActive=1 AND tl.TeamLeadName IS NOT NULL AND tl.TeamLeadName<>'' ORDER BY tl.TeamLeadName"

Write-Output "=== DONE ==="
