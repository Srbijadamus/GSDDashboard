# PS1_44_DiagEmptyNames.ps1 - Diagnose empty-name vacation rows (read-only)
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

# 1. Check Employee table - do they have names?
Run-Query "Employees table - name fields" @"
SELECT EmployeeId, FullName, FirstName, LastName, PrimaryRole, IsActive, TeamLeadName
FROM Employees
WHERE EmployeeId IN ('9130657','9132079','9117836','9106138','9126874','9074531','9129441')
ORDER BY EmployeeId
"@

# 2. Check Vacation rows for the same IDs - what names are stored on the vacation record itself?
Run-Query "Vacations table - name fields on the vacation rows themselves" @"
SELECT DISTINCT v.EmployeeId, v.FirstName AS Vac_FirstName, v.LastName AS Vac_LastName,
       e.FirstName AS Emp_FirstName, e.LastName AS Emp_LastName, e.FullName AS Emp_FullName
FROM Vacations v
LEFT JOIN Employees e ON e.EmployeeId = v.EmployeeId
WHERE v.EmployeeId IN ('9130657','9132079','9117836','9106138','9126874','9074531','9129441')
ORDER BY v.EmployeeId
"@

$conn.Close()
Write-Host "`nDone - NO changes made." -ForegroundColor Cyan
