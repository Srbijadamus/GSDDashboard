# PS1_21_DirectSQL.ps1
# Directly updates ShiftEntries via SQL for the 5 entries that could not be matched via API.
# Uses Windows Authentication (Trusted_Connection) - no credentials needed.
# Usage: powershell -ExecutionPolicy Bypass -File PS1_21_DirectSQL.ps1

$ErrorActionPreference = "Stop"
$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"

Write-Host "=== Direct SQL Update - ShiftEntries ===" -ForegroundColor Cyan
Write-Host ""

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)

try {
    $conn.Open()
    Write-Host "Connected to GSDDashboard on localhost\SQLEXPRESS." -ForegroundColor Gray
    Write-Host ""

    # Step 1: Confirm table and column names
    Write-Host "--- Column check: tables matching Shift% ---" -ForegroundColor Gray
    $cmdCheck = $conn.CreateCommand()
    $cmdCheck.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME LIKE '%Shift%' AND TABLE_NAME != 'WicOpeningHours' ORDER BY TABLE_NAME, ORDINAL_POSITION"
    $reader = $cmdCheck.ExecuteReader()
    while ($reader.Read()) {
        Write-Host "  $($reader["TABLE_NAME"]).$($reader["COLUMN_NAME"])" -ForegroundColor Gray
    }
    $reader.Close()
    Write-Host ""

    # Step 2: Run the 5 targeted UPDATEs by exact shift ID
    Write-Host "--- Applying updates ---" -ForegroundColor Cyan

    $updates = @(
        [PSCustomObject]@{ Id=25609; Name="Suhrab Sadieqy";  Start="08:00"; End="17:00" }
        [PSCustomObject]@{ Id=26043; Name="Viktor Winter";   Start="08:00"; End="17:00" }
        [PSCustomObject]@{ Id=26664; Name="Tunde Szabo";     Start="08:00"; End="17:00" }
        [PSCustomObject]@{ Id=21980; Name="Hamza Forrousso"; Start="08:00"; End="16:30" }
        [PSCustomObject]@{ Id=17300; Name="Duc Quy Huynh";   Start="07:00"; End="16:00" }
    )

    $totalUpdated = 0

    foreach ($u in $updates) {
        $cmdUpd = $conn.CreateCommand()
        $cmdUpd.CommandText = "UPDATE ShiftEntries SET ShiftStart='$($u.Start)', ShiftEnd='$($u.End)' WHERE Id=$($u.Id)"
        $rows = $cmdUpd.ExecuteNonQuery()
        if ($rows -gt 0) {
            Write-Host "  OK    $($u.Name.PadRight(22)) id=$($u.Id)  $($u.Start)-$($u.End)  ($rows row affected)" -ForegroundColor Green
            $totalUpdated += $rows
        }
        else {
            Write-Host "  WARN  $($u.Name.PadRight(22)) id=$($u.Id) - 0 rows affected (ID not found in DB)" -ForegroundColor Yellow
        }
    }

    Write-Host ""
    Write-Host "---------------------------------------------" -ForegroundColor Gray
    Write-Host "  Total rows updated: $totalUpdated" -ForegroundColor Green
    Write-Host "---------------------------------------------" -ForegroundColor Gray
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if ($conn.State -eq "Open") {
        $conn.Close()
        Write-Host ""
        Write-Host "Connection closed." -ForegroundColor Gray
    }
}
