# PS1_26_WicShiftEntriesColumns.ps1
# Show WicShiftEntries table columns to confirm schema before fix.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Write-Host ""
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "  PS1_26: WicShiftEntries Column Check" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
try {
    $conn.Open()

    # 1. All columns in WicShiftEntries
    Write-Host ""
    Write-Host "--- WicShiftEntries columns ---" -ForegroundColor Cyan
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WicShiftEntries'
ORDER BY ORDINAL_POSITION
"@
    $r = $cmd.ExecuteReader()
    while ($r.Read()) {
        Write-Host ("  {0,-30} {1,-15} nullable={2}  maxlen={3}" -f
            $r["COLUMN_NAME"], $r["DATA_TYPE"],
            $r["IS_NULLABLE"],
            $(if ($r.IsDBNull(3)) { "-" } else { $r["CHARACTER_MAXIMUM_LENGTH"] }))
    }
    $r.Close()

    # 2. Confirm no UNIQUE constraint on EmployeeId+ShiftDate
    Write-Host ""
    Write-Host "--- Indexes on WicShiftEntries ---" -ForegroundColor Cyan
    $cmd2 = $conn.CreateCommand()
    $cmd2.CommandText = @"
SELECT i.name AS IndexName, i.is_unique, c.name AS ColumnName
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c         ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE OBJECT_NAME(i.object_id) = 'WicShiftEntries'
ORDER BY i.name, ic.key_ordinal
"@
    $r2 = $cmd2.ExecuteReader()
    while ($r2.Read()) {
        Write-Host ("  {0,-40} unique={1}  col={2}" -f $r2["IndexName"], $r2["is_unique"], $r2["ColumnName"])
    }
    $r2.Close()

    # 3. Sample rows to see SupportLocation format
    Write-Host ""
    Write-Host "--- WicShiftEntries top 5 (IsOnSite=1) ---" -ForegroundColor Cyan
    $cmd3 = $conn.CreateCommand()
    $cmd3.CommandText = "SELECT TOP 5 EmployeeId, ShiftDate, SupportLocation, WorkingShift, IsOnSite, Task FROM WicShiftEntries WHERE IsOnSite = 1 ORDER BY ShiftDate DESC"
    $r3 = $cmd3.ExecuteReader()
    while ($r3.Read()) {
        Write-Host ("  {0}  {1}  SupportLocation={2}  WorkingShift={3}  Task={4}" -f
            $r3["EmployeeId"], $r3["ShiftDate"], $r3["SupportLocation"], $r3["WorkingShift"], $r3["Task"])
    }
    $r3.Close()

} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
} finally {
    $conn.Close()
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "  PS1_26 complete" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
