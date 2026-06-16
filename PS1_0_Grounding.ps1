# PS1_0_Grounding.ps1 — Run grounding SQL queries. Run in PowerShell 7.6.
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function Q($sql, $label) {
    Write-Host "`n=== $label ===" -ForegroundColor Cyan
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
        $reader = $cmd.ExecuteReader()
        $rows = @()
        while ($reader.Read()) {
            $row = [ordered]@{}
            for ($i = 0; $i -lt $reader.FieldCount; $i++) { $row[$reader.GetName($i)] = $reader.GetValue($i) }
            $rows += [pscustomobject]$row
        }
        $reader.Close(); $conn.Close()
        if ($rows.Count -eq 0) { Write-Host "(no rows)" -ForegroundColor Yellow }
        else { $rows | Format-Table -AutoSize }
    } catch { Write-Host "ERROR: $_" -ForegroundColor Red }
}

Q "SELECT COUNT(*) AS WicLocationCount FROM WicLocations" "1. WicLocations total count"
Q "SELECT COUNT(*) AS WicLocationActiveCount FROM WicLocations WHERE IsActive=1" "2. WicLocations active count"
Q "SELECT COUNT(*) AS AllEmployees FROM Employees" "3. Employees total"
Q "SELECT COUNT(*) AS ActiveEmployees FROM Employees WHERE IsActive=1" "4. Employees active"
Q @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WicLocations'
ORDER BY ORDINAL_POSITION
"@ "5. WicLocations schema (confirm Coordinates / MinAgentsRequired presence)"
Q "SELECT DISTINCT AssignmentType FROM WicAgentAssignments ORDER BY AssignmentType" "6. WicAgentAssignments.AssignmentType values"
Q @"
SELECT DISTINCT PrimaryRole FROM Employees WHERE IsActive=1 AND PrimaryRole IS NOT NULL
ORDER BY PrimaryRole
"@ "7. Employee.PrimaryRole distinct values (active only)"
Q "SELECT TOP 5 LocationCode, DisplayName, City, Country FROM WicLocations ORDER BY Country, City" "8. Sample WicLocations rows"
Q @"
SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Employees'
ORDER BY ORDINAL_POSITION
"@ "9. Employees schema"
Q "SELECT COUNT(*) AS WicOpeningHoursCount FROM WicOpeningHours" "10. WicOpeningHours count"
Q @"
SELECT TOP 10 LocationCode, DayOfWeek, OpenTime, CloseTime, OpenTime2, CloseTime2, IsClosed
FROM WicOpeningHours ORDER BY LocationCode, DayOfWeek
"@ "11. Sample WicOpeningHours (verify DayOfWeek encoding and split-block)"

Write-Host "`n=== Done ===" -ForegroundColor Green
