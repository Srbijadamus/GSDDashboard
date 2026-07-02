# PS1_45_DiagEmployeeLocation.ps1 - Employee location/Bundesland diagnostic (read-only)
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

Run-Query "1. Count employees missing Bundesland" @"
SELECT COUNT(*) AS MissingBundesland FROM Employees
WHERE Bundesland IS NULL OR Bundesland = ''
"@

Run-Query "2. Active employees missing Bundesland" @"
SELECT EmployeeId, FullName, PrimaryRole, Bundesland
FROM Employees
WHERE (Bundesland IS NULL OR Bundesland = '') AND IsActive = 1
ORDER BY PrimaryRole, FullName
"@

Run-Query "3. All columns in Employees table" @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Employees'
ORDER BY COLUMN_NAME
"@

$conn.Close()
Write-Host "`nDone - NO changes made." -ForegroundColor Cyan
