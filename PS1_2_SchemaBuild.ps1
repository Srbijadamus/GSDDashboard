# PS1_2_SchemaBuild.ps1
# 1. ALTER TABLE WicLocations to add Coordinates, MinAgentsRequired, Bundesland.
# 2. dotnet build with new model fields.
# Run AFTER PS1_1_Build.ps1 confirmed success.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function Exec-Sql($sql, $label) {
    Write-Host "`n--- $label ---" -ForegroundColor Cyan
    $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn.Open()
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Close()
    Write-Host "OK"
}

function Q($sql, $label) {
    Write-Host "`n--- $label ---" -ForegroundColor Cyan
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
    $rows | Format-Table -AutoSize
}

Write-Host "=== Step 1: Add columns to WicLocations (idempotent) ===" -ForegroundColor Yellow

foreach ($col in @(
    @{ Name = "Coordinates";       Def = "NVARCHAR(50) NULL" },
    @{ Name = "MinAgentsRequired"; Def = "INT NULL" },
    @{ Name = "Bundesland";        Def = "NVARCHAR(50) NULL" }
)) {
    $check = @"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME='WicLocations' AND COLUMN_NAME='$($col.Name)'
)
    ALTER TABLE WicLocations ADD $($col.Name) $($col.Def);
"@
    Exec-Sql $check "Add column: $($col.Name)"
}

Q @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WicLocations'
ORDER BY ORDINAL_POSITION
"@ "Verify WicLocations schema after ALTER"

Write-Host "`n=== Step 2: Kill process + rebuild ===" -ForegroundColor Yellow
Get-Process GSDDashboard.API -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Push-Location "C:\GSDDashboard\Backend"
dotnet build -c Debug 2>&1
$ok = $LASTEXITCODE -eq 0
Pop-Location

if (-not $ok) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }

Write-Host "`nDLL timestamp: $((Get-Item 'C:\GSDDashboard\Backend\bin\Debug\net8.0\GSDDashboard.API.dll').LastWriteTime)" -ForegroundColor Green

Write-Host "`n=== Step 3: Launch + verify new endpoints ===" -ForegroundColor Yellow
Start-Process `
    -FilePath "C:\GSDDashboard\Backend\bin\Debug\net8.0\GSDDashboard.API.exe" `
    -WorkingDirectory "C:\GSDDashboard\Backend" `
    -WindowStyle Minimized
Start-Sleep -Seconds 5

Write-Host "--- GET /api/wic/open?horizon=3 ---" -ForegroundColor Cyan
try {
    $open = Invoke-RestMethod "http://localhost:5000/api/wic/open?horizon=3"
    Write-Host "Days returned: $($open.Count)"
    Write-Host "Day[0] location count: $($open[0].locations.Count)"
    $open[0].locations | Where-Object { $_.status -ne 'CLOSED' } | Select-Object -First 3 |
        Select-Object locationCode, displayName, isOpen, coverageStatus, scheduledCount, effectiveCoverage |
        Format-Table -AutoSize
} catch { Write-Host "ERROR: $_" -ForegroundColor Red }

Write-Host "--- GET /api/wic/backup?locationCode=DE_Augsburg&horizon=3 ---" -ForegroundColor Cyan
try {
    $backup = Invoke-RestMethod "http://localhost:5000/api/wic/backup?locationCode=DE_Augsburg&horizon=3"
    $backup | ConvertTo-Json -Depth 5
} catch { Write-Host "ERROR: $_" -ForegroundColor Red }

Write-Host "--- GET /api/overview/wic-status?horizon=3 ---" -ForegroundColor Cyan
try {
    $wicStatus = Invoke-RestMethod "http://localhost:5000/api/overview/wic-status?horizon=3"
    Write-Host "Horizon: $($wicStatus.horizon)"
    $wicStatus.days | ForEach-Object {
        Write-Host "Date: $($_.date)  AtRisk: $($_.atRiskCount)"
        $_.locations | Where-Object { $_.status -ne 'CLOSED' } | Select-Object -First 3 |
            Select-Object locationCode, status, effectiveCoverage, topSubstitute |
            Format-Table -AutoSize
    }
} catch { Write-Host "ERROR: $_" -ForegroundColor Red }

Write-Host "`n=== Done ===" -ForegroundColor Green
