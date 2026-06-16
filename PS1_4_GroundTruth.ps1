# PS1_4_GroundTruth.ps1
# Runs all Step 2+3 ground-truth queries from the new prompt.
# Outputs are structured for copy-paste into the handoff document.
# Run from PowerShell 7.6. No git commands.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function Q($sql, $label) {
    Write-Host "`n========================================" -ForegroundColor DarkGray
    Write-Host "  $label" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor DarkGray
    Write-Host "SQL: $sql" -ForegroundColor DarkGray
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 30
        $reader = $cmd.ExecuteReader()
        $rows = @()
        while ($reader.Read()) {
            $row = [ordered]@{}
            for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                $val = $reader.GetValue($i)
                $row[$reader.GetName($i)] = if ($val -is [DBNull]) { "NULL" } else { $val }
            }
            $rows += [pscustomobject]$row
        }
        $reader.Close(); $conn.Close()
        if ($rows.Count -eq 0) { Write-Host "(no rows)" -ForegroundColor Yellow }
        else { $rows | Format-Table -AutoSize -Wrap }
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
    }
}

# ─── STEP 2: DB ground truth ────────────────────────────────────────────────

Q "SELECT COUNT(*) AS WicLocationCount FROM WicLocations" `
  "2.1 COUNT WicLocations (docs claim 40)"

Q "SELECT COUNT(*) AS WicLocationsActive FROM WicLocations WHERE IsActive=1" `
  "2.2 COUNT WicLocations active only"

Q "SELECT COUNT(*) AS EmployeeCount FROM Employees" `
  "2.3 COUNT Employees total (docs claim ~122)"

Q "SELECT COUNT(*) AS EmployeeActiveCount FROM Employees WHERE IsActive=1" `
  "2.4 COUNT Employees active"

Q @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WicLocations'
ORDER BY ORDINAL_POSITION
"@ "2.5 WicLocations schema (check for Coordinates/MinAgentsRequired/Bundesland)"

Q @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Employees'
ORDER BY ORDINAL_POSITION
"@ "2.6 Employees schema (check for Bundesland, PLZ, email, phone)"

Q @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'SickLeaves'
ORDER BY ORDINAL_POSITION
"@ "2.7 SickLeaves schema (confirm FirstDay/LastDay, LeaveType column name)"

Q "SELECT DISTINCT AssignmentType FROM WicAgentAssignments ORDER BY AssignmentType" `
  "2.8 WicAgentAssignments.AssignmentType distinct values"

Q @"
SELECT DISTINCT PrimaryRole FROM Employees
WHERE PrimaryRole IS NOT NULL
ORDER BY PrimaryRole
"@ "2.9 Employees.PrimaryRole distinct values"

# NOTE: actual column names are LocationCode + DisplayName; Latitude/Longitude don't exist.
# Coordinates is a single "lat,lon" varchar column added by PS1_2_SchemaBuild.ps1.
Q @"
SELECT TOP 5 LocationCode, DisplayName, PostalCode, City, Country,
    CASE WHEN COL_LENGTH('WicLocations','Bundesland') IS NOT NULL
         THEN CAST(Bundesland AS NVARCHAR(50)) ELSE '(column missing)' END AS Bundesland,
    CASE WHEN COL_LENGTH('WicLocations','Coordinates') IS NOT NULL
         THEN CAST(Coordinates AS NVARCHAR(100)) ELSE '(column missing)' END AS Coordinates,
    CASE WHEN COL_LENGTH('WicLocations','MinAgentsRequired') IS NOT NULL
         THEN CAST(MinAgentsRequired AS NVARCHAR(10)) ELSE '(column missing)' END AS MinAgentsRequired
FROM WicLocations ORDER BY LocationCode
"@ "2.10 WicLocations TOP 5 (shows new cols if PS1_2 was run)"

Q @"
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME LIKE '%Skill%'
   OR TABLE_NAME LIKE '%Qual%'
   OR TABLE_NAME LIKE '%Lang%'
ORDER BY TABLE_NAME
"@ "2.11 Skill/Qual/Lang tables (expect none)"

Q @"
SELECT DISTINCT LeaveType FROM SickLeaves
WHERE LeaveType IS NOT NULL
ORDER BY LeaveType
"@ "2.12 SickLeaves.LeaveType distinct values"

Q @"
SELECT TOP 3 LocationCode, DisplayName, PostalCode, City, Country
FROM WicLocations
WHERE LocationCode LIKE '%RENDSBURG%'
   OR LocationCode LIKE '%PFAFF%'
   OR LocationCode LIKE '%Regensburg%'
   OR DisplayName LIKE '%Rendsburg%'
   OR DisplayName LIKE '%Pfaff%'
   OR DisplayName LIKE '%Regensburg%'
"@ "2.13 Extra locations not in schema.sql seed (Rendsburg/Pfaffenhofen/Regensburg)"

Q @"
SELECT COUNT(*) AS WicOpeningHoursRows FROM WicOpeningHours
"@ "2.14 WicOpeningHours row count"

# ─── STEP 3: Live system check ───────────────────────────────────────────────

Write-Host "`n========================================" -ForegroundColor DarkGray
Write-Host "  3.1 GET /health" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor DarkGray
try {
    $h = Invoke-RestMethod "http://localhost:5000/health" -TimeoutSec 10
    $h | ConvertTo-Json -Depth 3
} catch { Write-Host "ERROR: $_" -ForegroundColor Red }

Write-Host "`n========================================" -ForegroundColor DarkGray
Write-Host "  3.2 GET /api/wic/locations (first 3 items)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor DarkGray
try {
    $locs = Invoke-RestMethod "http://localhost:5000/api/wic/locations" -TimeoutSec 15
    Write-Host "Total count: $($locs.Count)"
    $locs[0..2] | ConvertTo-Json -Depth 4
} catch { Write-Host "ERROR: $_" -ForegroundColor Red }

Write-Host "`n========================================" -ForegroundColor DarkGray
Write-Host "  3.3 Route group registrations in Program.cs" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor DarkGray
Select-String -Path "C:\GSDDashboard\Backend\Program.cs" -Pattern "MapGroup|MapGet|MapPost|MapPatch|MapDelete|MapEndpoints" |
    ForEach-Object { "$($_.LineNumber): $($_.Line.Trim())" }

Write-Host "`n========================================" -ForegroundColor DarkGray
Write-Host "  3.4 Controller files check (expect none — Minimal API project)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor DarkGray
$ctrl = Get-ChildItem "C:\GSDDashboard\Backend" -Filter "*Controller.cs" -Recurse -ErrorAction SilentlyContinue
if ($ctrl) { $ctrl | Select-Object FullName } else { Write-Host "(no *Controller.cs files — confirmed Minimal APIs, no MVC controllers)" }

Write-Host "`n========================================" -ForegroundColor DarkGray
Write-Host "  COMPLETE — paste this output into handoff_prompt1.md" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor DarkGray
