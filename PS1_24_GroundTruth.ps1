# PS1_24_GroundTruth.ps1
# Phase 1: Ground Truth Report - all facts before any feature code is written.
# Uses System.Data.SqlClient (no Invoke-Sqlcmd, no Unicode, no here-strings).

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function RunQuery {
    param([string]$Label, [string]$Sql)
    Write-Host ""
    Write-Host "--- $Label ---" -ForegroundColor Cyan
    $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Sql
        $cmd.CommandTimeout = 30
        $r = $cmd.ExecuteReader()
        $cols = @()
        for ($i = 0; $i -lt $r.FieldCount; $i++) { $cols += $r.GetName($i) }
        Write-Host ($cols -join "  |  ") -ForegroundColor DarkGray
        Write-Host ("-" * 60) -ForegroundColor DarkGray
        $rowCount = 0
        while ($r.Read()) {
            $vals = @()
            for ($i = 0; $i -lt $r.FieldCount; $i++) {
                $v = if ($r.IsDBNull($i)) { "NULL" } else { [string]$r[$i] }
                $vals += $v
            }
            Write-Host ($vals -join "  |  ")
            $rowCount++
        }
        Write-Host "($rowCount rows)" -ForegroundColor DarkGray
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
    } finally {
        $conn.Close()
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "  PS1_24: GROUND TRUTH REPORT" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow

# 1. Row counts
RunQuery "1a. WicLocations row count" "SELECT COUNT(*) AS WicLocationCount FROM WicLocations"
RunQuery "1b. Employees row count" "SELECT COUNT(*) AS EmployeeCount FROM Employees"
RunQuery "1c. WicOpeningHours row count" "SELECT COUNT(*) AS OpeningHoursCount FROM WicOpeningHours"
RunQuery "1d. WicAgentAssignments row count" "SELECT COUNT(*) AS AssignmentCount FROM WicAgentAssignments"
RunQuery "1e. ShiftEntries row count" "SELECT COUNT(*) AS ShiftEntryCount FROM ShiftEntries"
RunQuery "1f. SickLeaves row count" "SELECT COUNT(*) AS SickLeaveCount FROM SickLeaves"

# 2. WicLocations schema - critical: Coordinates, MinAgentsRequired, Bundesland
RunQuery "2. WicLocations columns" @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WicLocations'
ORDER BY ORDINAL_POSITION
"@

# 3. Employees schema - address, PLZ, city, Bundesland
RunQuery "3. Employees columns" @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Employees'
ORDER BY ORDINAL_POSITION
"@

# 4. SickLeaves schema - StartDate/EndDate for multi-day
RunQuery "4. SickLeaves columns" @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'SickLeaves'
ORDER BY ORDINAL_POSITION
"@

# 5. ShiftEntries schema
RunQuery "5. ShiftEntries columns" @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'ShiftEntries'
ORDER BY ORDINAL_POSITION
"@

# 6. Assignment types
RunQuery "6. Distinct AssignmentType in WicAgentAssignments" @"
SELECT AssignmentType, COUNT(*) AS Count
FROM WicAgentAssignments
GROUP BY AssignmentType
ORDER BY Count DESC
"@

# 7. Employee roles
RunQuery "7. Distinct PrimaryRole in Employees" @"
SELECT PrimaryRole, COUNT(*) AS Count
FROM Employees
GROUP BY PrimaryRole
ORDER BY Count DESC
"@

# 8. Check if Coordinates/MinAgentsRequired/Bundesland exist and have data
RunQuery "8a. WicLocations - Coordinates sample (top 5)" @"
SELECT TOP 5 LocationCode, Coordinates, MinAgentsRequired
FROM WicLocations
ORDER BY LocationCode
"@

RunQuery "8b. WicLocations - Bundesland (if column exists)" @"
SELECT
    SUM(CASE WHEN Bundesland IS NOT NULL AND Bundesland <> '' THEN 1 ELSE 0 END) AS BundeslandPopulated,
    SUM(CASE WHEN Bundesland IS NULL OR Bundesland = '' THEN 1 ELSE 0 END) AS BundeslandNull,
    COUNT(*) AS Total
FROM WicLocations
"@

# 9. Employees - address/PLZ/city/Bundesland fields
RunQuery "9. Employees - location fields sample (top 5)" @"
SELECT TOP 5 EmployeeId, FirstName, LastName, PrimaryRole,
    CASE WHEN EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='PostalCode') THEN 'HasPLZ' ELSE 'NoPLZ' END AS PLZCheck
FROM Employees
ORDER BY EmployeeId
"@

# 10. Check for skill/qualification tables
RunQuery "10. All tables in GSDDashboard DB" @"
SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME
"@

# 11. WicLocations - NL vs DE split
RunQuery "11. WicLocations by Country" @"
SELECT Country, COUNT(*) AS Count
FROM WicLocations
GROUP BY Country
ORDER BY Count DESC
"@

# 12. SubstitutionHistory schema (if exists)
RunQuery "12. SubstitutionHistory columns (if exists)" @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'SubstitutionHistory'
ORDER BY ORDINAL_POSITION
"@

# 13. ShiftTypes in use
RunQuery "13. Distinct ShiftType values in ShiftEntries" @"
SELECT ShiftType, COUNT(*) AS Count
FROM ShiftEntries
GROUP BY ShiftType
ORDER BY Count DESC
"@

# 14. SickLeaves - StartDate/EndDate check and sample
RunQuery "14. SickLeaves top 5 sample" @"
SELECT TOP 5 *
FROM SickLeaves
ORDER BY 1
"@

# 15. WicAgentAssignments - sample with location codes
RunQuery "15. WicAgentAssignments top 5" @"
SELECT TOP 5 *
FROM WicAgentAssignments
ORDER BY 1
"@

# 16. WicLocations - Augsburg row (Bundesland sanity check)
RunQuery "16. Augsburg location row" @"
SELECT LocationCode, DisplayName, City, Country, Coordinates, MinAgentsRequired, Bundesland
FROM WicLocations
WHERE City LIKE '%Augsburg%' OR LocationCode LIKE '%Augsburg%'
"@

# 17. Employees with SSP role sample
RunQuery "17. SSP employees (backlog pool candidates)" @"
SELECT TOP 10 EmployeeId, FirstName, LastName, PrimaryRole, SecondaryRole, Bundesland
FROM Employees
WHERE PrimaryRole = 'SSP'
ORDER BY EmployeeId
"@

# 18. Check contact fields on Employees
RunQuery "18. Contact fields check on Employees" @"
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Employees'
AND COLUMN_NAME IN ('Email','Phone','Mobile','PhoneNumber','EmailAddress','Address','Street','PostalCode','PLZ','ZipCode','City','Bundesland')
ORDER BY COLUMN_NAME
"@

# 19. Current date from DB (for reference)
RunQuery "19. DB server date" "SELECT GETDATE() AS ServerDateTime"

# ── Tunnel health checks ──────────────────────────────────────────────────────
Write-Host ""
Write-Host "--- 20. Tunnel health checks ---" -ForegroundColor Cyan

$dashboardUrl = "https://8nh5k5g1-5000.euw.devtunnels.ms"

$endpoints = @(
    "$dashboardUrl/health",
    "$dashboardUrl/api/wic/locations",
    "$dashboardUrl/api/wic/forecast?horizon=1",
    "$dashboardUrl/api/wic/briefing"
)

foreach ($url in $endpoints) {
    try {
        $resp = Invoke-WebRequest -Uri $url -TimeoutSec 10 -UseBasicParsing
        Write-Host ("  HTTP {0}  {1}" -f $resp.StatusCode, $url) -ForegroundColor Green
    } catch {
        $sc = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { "ERR" }
        Write-Host ("  HTTP {0}  {1}  [{2}]" -f $sc, $url, $_.Exception.Message) -ForegroundColor Red
    }
}

# ── Forecast JSON sample ──────────────────────────────────────────────────────
Write-Host ""
Write-Host "--- 21. Forecast JSON sample (first location) ---" -ForegroundColor Cyan
try {
    $forecast = Invoke-RestMethod "$dashboardUrl/api/wic/forecast?horizon=1" -TimeoutSec 15
    if ($forecast -and $forecast.Count -gt 0) {
        $first = $forecast[0]
        Write-Host "locationCode:  $($first.locationCode)"
        Write-Host "displayName:   $($first.displayName)"
        Write-Host "todayStatus:   $($first.todayStatus)"
        Write-Host "coordinates:   $($first.coordinates)"
        Write-Host "atRiskDays:    $($first.atRiskDays)"
        Write-Host "forecast days: $($first.forecast.Count)"
        if ($first.forecast.Count -gt 0) {
            $d = $first.forecast[0]
            Write-Host "  day[0].date:       $($d.date)"
            Write-Host "  day[0].status:     $($d.status)"
            Write-Host "  day[0].isAtRisk:   $($d.isAtRisk)"
            Write-Host "  day[0].agentCount: $($d.agentCount)"
        }
    } else {
        Write-Host "No locations returned or unexpected shape" -ForegroundColor Yellow
    }
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
}

# ── Route listing ─────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "--- 22. Program.cs route registrations ---" -ForegroundColor Cyan
Select-String -Path "C:\GSDDashboard\Backend\Program.cs" -Pattern "Map|app\." | ForEach-Object {
    Write-Host ("  L{0}: {1}" -f $_.LineNumber, $_.Line.Trim())
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "  GROUND TRUTH REPORT COMPLETE" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
