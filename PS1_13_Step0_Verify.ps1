# PS1_13_Step0_Verify.ps1
# Step 0-B: Show DayOfWeek values in WicOpeningHours to confirm convention (0=Sun or 1=Sun).
# Step 0-D: Show MinAgentsRequired in WicLocations; UPDATE to 1 if all NULL.
# No here-strings. No Unicode. Run from powershell.exe 5.1 or 7.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function RunQuery {
    param([string]$sql, [string]$label)
    Write-Host ""
    Write-Host ">>> $label" -ForegroundColor Cyan
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        $cmd.CommandTimeout = 15
        $r = $cmd.ExecuteReader()
        $rows = @()
        while ($r.Read()) {
            $row = [ordered]@{}
            for ($i = 0; $i -lt $r.FieldCount; $i++) {
                $v = $r.GetValue($i)
                $row[$r.GetName($i)] = if ($v -is [DBNull]) { "NULL" } else { $v }
            }
            $rows += [pscustomobject]$row
        }
        $r.Close()
        $conn.Close()
        if ($rows.Count -eq 0) {
            Write-Host "(no rows)" -ForegroundColor Yellow
        } else {
            $rows | Format-Table -AutoSize -Wrap
        }
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
    }
}

function RunScalar {
    param([string]$sql, [string]$label)
    Write-Host ""
    Write-Host ">>> $label" -ForegroundColor Cyan
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        $cmd.CommandTimeout = 15
        $result = $cmd.ExecuteScalar()
        $conn.Close()
        return $result
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
        return $null
    }
}

function RunNonQuery {
    param([string]$sql, [string]$label)
    Write-Host ""
    Write-Host ">>> $label" -ForegroundColor Cyan
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        $cmd.CommandTimeout = 15
        $n = $cmd.ExecuteNonQuery()
        $conn.Close()
        Write-Host "Rows affected: $n" -ForegroundColor Green
        return $n
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
        return 0
    }
}

# ── Step 0-B: DayOfWeek convention check ─────────────────────────────────────

Write-Host ""
Write-Host "=== STEP 0-B: WicOpeningHours DayOfWeek convention ===" -ForegroundColor Yellow

$sqlDow = "SELECT TOP 5 LocationCode, DayOfWeek, OpenTime, CloseTime FROM WicOpeningHours ORDER BY LocationCode, DayOfWeek"
RunQuery $sqlDow "TOP 5 rows from WicOpeningHours"

$sqlDowDist = "SELECT DISTINCT DayOfWeek FROM WicOpeningHours ORDER BY DayOfWeek"
RunQuery $sqlDowDist "All distinct DayOfWeek values in DB"

$minDow = RunScalar "SELECT MIN(DayOfWeek) FROM WicOpeningHours" "MIN(DayOfWeek)"
$maxDow = RunScalar "SELECT MAX(DayOfWeek) FROM WicOpeningHours" "MAX(DayOfWeek)"

Write-Host ""
Write-Host "DayOfWeek range: MIN=$minDow, MAX=$maxDow" -ForegroundColor Cyan
if ($maxDow -eq 6) {
    Write-Host "CONFIRMED: DB uses 0=Sun...6=Sat (.NET convention). GetOpenAsync is CORRECT." -ForegroundColor Green
    Write-Host "GetAvailableHoursAsync was using sqlDow (1=Mon...7=Sun) -- FIXED in Prompt 3 to use raw (int)DayOfWeek." -ForegroundColor Green
} elseif ($maxDow -eq 7) {
    Write-Host "WARNING: DB may use 1=Mon...7=Sun convention. GetOpenAsync may be WRONG for Sunday." -ForegroundColor Yellow
    Write-Host "Investigate: check how Sunday rows appear and compare with confirmed-working Monday queries." -ForegroundColor Yellow
} else {
    Write-Host "UNEXPECTED: DayOfWeek max=$maxDow. Check WicOpeningHours seeding." -ForegroundColor Red
}

# ── Step 0-D: MinAgentsRequired ──────────────────────────────────────────────

Write-Host ""
Write-Host "=== STEP 0-D: MinAgentsRequired in WicLocations ===" -ForegroundColor Yellow

$sqlMin = "SELECT LocationCode, DisplayName, MinAgentsRequired FROM WicLocations WHERE IsActive = 1 ORDER BY LocationCode"
RunQuery $sqlMin "WicLocations MinAgentsRequired (all active)"

$nullCount = RunScalar "SELECT COUNT(*) FROM WicLocations WHERE IsActive = 1 AND MinAgentsRequired IS NULL" "Count of NULLs"
$totalCount = RunScalar "SELECT COUNT(*) FROM WicLocations WHERE IsActive = 1" "Total active"

Write-Host ""
Write-Host "NULL: $nullCount / Total: $totalCount" -ForegroundColor Cyan

if ($nullCount -gt 0 -and $nullCount -eq $totalCount) {
    Write-Host "All MinAgentsRequired are NULL -- setting all to 1 (default minimum)." -ForegroundColor Yellow
    $updated = RunNonQuery "UPDATE WicLocations SET MinAgentsRequired = 1 WHERE IsActive = 1 AND MinAgentsRequired IS NULL" "UPDATE WicLocations SET MinAgentsRequired = 1"
    Write-Host "Updated $updated rows to MinAgentsRequired = 1." -ForegroundColor Green
} elseif ($nullCount -gt 0) {
    Write-Host "$nullCount rows still NULL -- only some locations have MinAgentsRequired set." -ForegroundColor Yellow
    Write-Host "Updating remaining NULLs to default of 1." -ForegroundColor Yellow
    RunNonQuery "UPDATE WicLocations SET MinAgentsRequired = 1 WHERE IsActive = 1 AND MinAgentsRequired IS NULL" "UPDATE remaining NULLs to 1"
} else {
    Write-Host "All MinAgentsRequired already set. No update needed." -ForegroundColor Green
}

# ── Final verification ────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Final state ===" -ForegroundColor Yellow
RunQuery "SELECT COUNT(*) AS TotalLocations, MIN(MinAgentsRequired) AS MinVal, MAX(MinAgentsRequired) AS MaxVal, SUM(CASE WHEN MinAgentsRequired IS NULL THEN 1 ELSE 0 END) AS NullCount FROM WicLocations WHERE IsActive = 1" "MinAgentsRequired summary"

Write-Host ""
Write-Host "=== PS1_13 complete ===" -ForegroundColor Green
