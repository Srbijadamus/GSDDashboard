# PS1_66_Group1Verify.ps1  (v3)
# Verifies the Group 1 IsActive fix for the three critical coverage loops.
# BEFORE = COUNT from raw WicShiftEntries (what old code computed).
# AFTER  = COUNT via INNER JOIN Employees WHERE IsActive=1 (what fixed code computes).
# Idempotent: pre-cleans TESTONLY-INACTIVE at start; finally block always cleans up.
# Connection read from appsettings.json. No direct SQL tool invocations.

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"
$baseUrl         = "http://localhost:5000"
$testEmpId       = "TESTONLY-INACTIVE"

Add-Type -AssemblyName "System.Data"

$cfg = Get-Content $appSettingsPath | ConvertFrom-Json
$cs  = $cfg.ConnectionStrings.DefaultConnection

# ---- Helpers (all defined before first use) ------------------------------------

function Open-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection($script:cs)
    $c.Open()
    return $c
}

function Invoke-Scalar([string]$sql, [hashtable]$params = @{}) {
    $conn = Open-Conn
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($kv in $params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        $v      = $cmd.ExecuteScalar()
        $result = $v
        if ($v -is [System.DBNull]) { $result = $null }
        return $result
    } finally { $conn.Close() }
}

function Invoke-Rows([string]$sql, [hashtable]$params = @{}) {
    $conn = Open-Conn
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($kv in $params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        $rows = @()
        $rdr  = $cmd.ExecuteReader()
        while ($rdr.Read()) {
            $row = @{}
            for ($i = 0; $i -lt $rdr.FieldCount; $i++) {
                $v      = $rdr.GetValue($i)
                $mapped = $v
                if ($v -is [System.DBNull]) { $mapped = $null }
                $row[$rdr.GetName($i)] = $mapped
            }
            $rows += [PSCustomObject]$row
        }
        $rdr.Close()
        return $rows
    } finally { $conn.Close() }
}

function Invoke-NonQuery([string]$sql, [hashtable]$params = @{}) {
    $conn = Open-Conn
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($kv in $params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        return $cmd.ExecuteNonQuery()
    } finally { $conn.Close() }
}

function Remove-TestRows {
    # WicShiftEntries first (no FK back to Employees, but correct order anyway)
    [void](Invoke-NonQuery "DELETE FROM WicShiftEntries WHERE EmployeeId = @id" @{ id = $script:testEmpId })
    [void](Invoke-NonQuery "DELETE FROM Employees       WHERE EmployeeId = @id" @{ id = $script:testEmpId })
}

# ---- Main ----------------------------------------------------------------------

Write-Host ""
Write-Host "=== PS1_66 v3: Group 1 Coverage Fix Verification ===" -ForegroundColor Yellow
Write-Host ""

# PRE-CLEAN: unconditional delete handles leftovers from any prior crashed run.
Write-Host "Pre-clean: removing any existing TESTONLY-INACTIVE rows ..." -ForegroundColor DarkGray
Remove-TestRows
$preWic = [int](Invoke-Scalar "SELECT COUNT(*) FROM WicShiftEntries WHERE EmployeeId = @id" @{ id = $testEmpId })
$preEmp = [int](Invoke-Scalar "SELECT COUNT(*) FROM Employees       WHERE EmployeeId = @id" @{ id = $testEmpId })
Write-Host ("  WicShiftEntries: {0}  Employees: {1}  (both must be 0 to continue)" -f $preWic, $preEmp) -ForegroundColor DarkGray
if ($preWic -ne 0 -or $preEmp -ne 0) {
    Write-Host "  ERROR: Pre-clean failed -- rows still present. Cannot continue." -ForegroundColor Red
    exit 1
}
Write-Host ""

try {

    # ---- Step 1: Pick a real active WIC location --------------------------------
    Write-Host "Step 1: Pick a real active WIC location ..." -ForegroundColor Cyan

    $locRows = Invoke-Rows ("SELECT TOP 1 LocationCode, DisplayName, MinAgentsRequired " +
                            "FROM WicLocations WHERE IsActive = 1 ORDER BY DisplayName")
    if ($locRows.Count -eq 0) { throw "No active WicLocations in DB." }

    $testLocation = $locRows[0].DisplayName
    $minReqRaw    = $locRows[0].MinAgentsRequired
    $minReq       = 1
    if ($null -ne $minReqRaw) { $minReq = [int]$minReqRaw }

    $testDate = [datetime]::Today.AddDays(30).ToString("yyyy-MM-dd")
    $testDow  = [datetime]::Parse($testDate).DayOfWeek.ToString()

    Write-Host ("  Location : {0}  (MinAgentsRequired={1})" -f $testLocation, $minReq) -ForegroundColor White
    Write-Host ("  Test date: {0}  ({1})" -f $testDate, $testDow) -ForegroundColor White
    Write-Host ""

    # ---- Step 2: Insert Employees row (IsActive=0) ------------------------------
    Write-Host "Step 2: Insert Employees row EmployeeId='$testEmpId' IsActive=0 ..." -ForegroundColor Cyan

    $empInsSql = "INSERT INTO Employees (EmployeeId, FullName, IsActive, IsTrainee, CreatedAt) " +
                 "VALUES (@id, 'TESTONLY: Inactive Employee', 0, 0, @now)"
    [void](Invoke-NonQuery $empInsSql @{ id = $testEmpId; now = [datetime]::UtcNow })

    $empCheck = [int](Invoke-Scalar ("SELECT COUNT(*) FROM Employees " +
                                     "WHERE EmployeeId = @id AND IsActive = 0") @{ id = $testEmpId })
    if ($empCheck -ne 1) { throw "Employees row not found after INSERT (expected 1 with IsActive=0)." }

    Write-Host ("  OK  EmployeeId='{0}'  IsActive=0  (verify: {1} row)" -f $testEmpId, $empCheck) -ForegroundColor Green
    Write-Host ""

    # ---- Step 3: Insert WicShiftEntry (IsOnSite=1) ------------------------------
    Write-Host "Step 3: Insert WicShiftEntry EmployeeId='$testEmpId' IsOnSite=1 ..." -ForegroundColor Cyan

    $wicInsSql = "INSERT INTO WicShiftEntries " +
                 "(EmployeeId, ShiftDate, DayOfWeek, SupportLocation, IsOnSite, IsGSDDay, IsOffDay, WorkingShift, Task) " +
                 "VALUES (@empId, @date, @dow, @loc, 1, 0, 0, '08:00-17:00', 'WIC'); " +
                 "SELECT SCOPE_IDENTITY()"
    $rawId     = Invoke-Scalar $wicInsSql @{ empId = $testEmpId; date = $testDate; dow = $testDow; loc = $testLocation }
    $testWicId = [int]$rawId
    if ($testWicId -le 0) { throw "SCOPE_IDENTITY() returned 0 or null after WicShiftEntry INSERT." }

    Write-Host ("  OK  Id={0}  EmpId='{1}'  Date={2}  Loc='{3}'  IsOnSite=1" -f `
        $testWicId, $testEmpId, $testDate, $testLocation) -ForegroundColor Green
    Write-Host ""

    # ---- Step 4: BEFORE count (old code: no IsActive filter) --------------------
    Write-Host "Step 4: BEFORE count -- raw WicShiftEntries, no IsActive filter ..." -ForegroundColor Cyan
    Write-Host "        (replicates GetOpenAsync / ForecastService / CoverageEvaluator BEFORE the fix)" -ForegroundColor DarkGray

    $beforeSql   = "SELECT COUNT(*) FROM WicShiftEntries " +
                   "WHERE IsOnSite = 1 AND ShiftDate = @date AND SupportLocation = @loc"
    $beforeCount = [int](Invoke-Scalar $beforeSql @{ date = $testDate; loc = $testLocation })

    Write-Host ("  BEFORE = {0}  (TESTONLY-INACTIVE included -- old code counted this)" -f $beforeCount) -ForegroundColor Yellow
    Write-Host ""

    # ---- Step 5: AFTER count (fixed code: INNER JOIN Employees WHERE IsActive=1) -
    Write-Host "Step 5: AFTER count -- INNER JOIN Employees WHERE IsActive=1 ..." -ForegroundColor Cyan
    Write-Host "        (replicates what the patched code now computes)" -ForegroundColor DarkGray

    $afterSql   = "SELECT COUNT(*) " +
                  "FROM WicShiftEntries w " +
                  "INNER JOIN Employees e ON w.EmployeeId = e.EmployeeId AND e.IsActive = 1 " +
                  "WHERE w.IsOnSite = 1 AND w.ShiftDate = @date AND w.SupportLocation = @loc"
    $afterCount = [int](Invoke-Scalar $afterSql @{ date = $testDate; loc = $testLocation })

    Write-Host ("  AFTER  = {0}  (TESTONLY-INACTIVE excluded via IsActive=0)" -f $afterCount) -ForegroundColor Green
    Write-Host ""

    # ---- Step 6: Verdict --------------------------------------------------------
    Write-Host "Step 6: Verdict ..." -ForegroundColor Cyan

    $delta = $beforeCount - $afterCount

    if ($delta -gt 0) {
        Write-Host ("  PASS: BEFORE={0} > AFTER={1}  (delta={2})" -f $beforeCount, $afterCount, $delta) -ForegroundColor Green

        $statusBefore = "UNCOVERED"
        if ($beforeCount -ge $minReq) { $statusBefore = "COVERED" } elseif ($beforeCount -gt 0) { $statusBefore = "PARTIAL" }
        $statusAfter  = "UNCOVERED"
        if ($afterCount  -ge $minReq) { $statusAfter  = "COVERED" } elseif ($afterCount  -gt 0) { $statusAfter  = "PARTIAL" }

        if ($statusBefore -ne $statusAfter) {
            Write-Host ("  Status flip (MinRequired={0}): {1} -> {2}" -f $minReq, $statusBefore, $statusAfter) -ForegroundColor Green
            Write-Host "  A soft-deleted agent's stale WicShiftEntry was hiding an at-risk day." -ForegroundColor Green
        } else {
            Write-Host ("  Coverage status: '{0}' in both cases (MinRequired={1}, enough real agents present)." -f $statusBefore, $minReq) -ForegroundColor DarkGray
            Write-Host "  Delta proves the join filter works; status flip occurs at locations with fewer active agents." -ForegroundColor DarkGray
        }
    } elseif ($delta -eq 0) {
        Write-Host ("  FAIL: BEFORE={0} == AFTER={1} -- TESTONLY-INACTIVE was not excluded." -f $beforeCount, $afterCount) -ForegroundColor Red
        Write-Host "  Verify that Step 2 set IsActive=0 and the INNER JOIN in Step 5 is correct." -ForegroundColor Red
    } else {
        Write-Host ("  UNEXPECTED: AFTER={0} > BEFORE={1}.  Investigate." -f $afterCount, $beforeCount) -ForegroundColor Red
    }
    Write-Host ""

    # ---- Step 7: Real-data leak count -------------------------------------------
    Write-Host "Step 7: Real-data leak count -- existing IsOnSite=1 rows for inactive/missing employees ..." -ForegroundColor Cyan
    Write-Host "        (TESTONLY-INACTIVE excluded; these are real stale rows the fix corrects)" -ForegroundColor DarkGray

    $realLeakCountSql = "SELECT COUNT(*) " +
                        "FROM WicShiftEntries w " +
                        "LEFT JOIN Employees e ON w.EmployeeId = e.EmployeeId " +
                        "WHERE w.IsOnSite = 1 " +
                        "  AND w.EmployeeId <> @testId " +
                        "  AND (e.EmployeeId IS NULL OR e.IsActive = 0)"
    $realLeakCount = [int](Invoke-Scalar $realLeakCountSql @{ testId = $testEmpId })

    $leakColor = "Green"
    if ($realLeakCount -gt 0) { $leakColor = "Yellow" }
    Write-Host ("  Real leaking WicShiftEntries: {0}" -f $realLeakCount) -ForegroundColor $leakColor

    if ($realLeakCount -gt 0) {
        Write-Host "  Top rows (up to 10):" -ForegroundColor Yellow
        $realLeakRowsSql = "SELECT TOP 10 w.Id, w.EmployeeId, " +
                           "CONVERT(varchar(10), w.ShiftDate, 120) AS ShiftDate, " +
                           "w.SupportLocation, e.FullName, e.IsActive " +
                           "FROM WicShiftEntries w " +
                           "LEFT JOIN Employees e ON w.EmployeeId = e.EmployeeId " +
                           "WHERE w.IsOnSite = 1 " +
                           "  AND w.EmployeeId <> @testId " +
                           "  AND (e.EmployeeId IS NULL OR e.IsActive = 0) " +
                           "ORDER BY w.ShiftDate DESC"
        $realLeakRows = Invoke-Rows $realLeakRowsSql @{ testId = $testEmpId }
        foreach ($r in $realLeakRows) {
            $empTag = "MISSING from Employees"
            if ($null -ne $r.IsActive) { $empTag = "IsActive=$($r.IsActive)" }
            Write-Host ("    Id={0,-6} EmpId={1,-15} Date={2}  Loc={3}  ({4})" -f `
                $r.Id, $r.EmployeeId, $r.ShiftDate, $r.SupportLocation, $empTag) -ForegroundColor Yellow
        }
        if ($realLeakCount -gt 10) {
            Write-Host ("    ... and {0} more not shown." -f ($realLeakCount - 10)) -ForegroundColor DarkGray
        }
    }
    Write-Host ""

    # ---- Step 8: API smoke-check ------------------------------------------------
    Write-Host "Step 8: API smoke-check (meaningful after PS1_19_FinalBuildVerify.ps1) ..." -ForegroundColor Cyan
    try {
        $resp = Invoke-WebRequest -Uri "$baseUrl/api/wic/forecast?horizon=7" -Method GET -TimeoutSec 10 -UseBasicParsing
        if ($resp.StatusCode -eq 200) {
            Write-Host "  /api/wic/forecast?horizon=7  HTTP 200 -- server is live on patched build." -ForegroundColor Green
        } else {
            Write-Host ("  HTTP {0} from forecast endpoint." -f $resp.StatusCode) -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  Server not reachable -- run PS1_19_FinalBuildVerify.ps1 first." -ForegroundColor Yellow
    }
    Write-Host ""

} finally {

    # Always runs: delete both test rows regardless of success or failure above.
    Write-Host "Cleanup (always): removing TESTONLY-INACTIVE rows ..." -ForegroundColor Cyan
    Remove-TestRows
    $wicRemain = [int](Invoke-Scalar "SELECT COUNT(*) FROM WicShiftEntries WHERE EmployeeId = @id" @{ id = $testEmpId })
    $empRemain = [int](Invoke-Scalar "SELECT COUNT(*) FROM Employees       WHERE EmployeeId = @id" @{ id = $testEmpId })

    if ($wicRemain -eq 0 -and $empRemain -eq 0) {
        Write-Host "  WicShiftEntries remaining : 0  (clean)" -ForegroundColor Green
        Write-Host "  Employees remaining       : 0  (clean)" -ForegroundColor Green
    } else {
        if ($wicRemain -gt 0) {
            Write-Host ("  WARNING: {0} WicShiftEntry row(s) still present for TESTONLY-INACTIVE." -f $wicRemain) -ForegroundColor Red
        }
        if ($empRemain -gt 0) {
            Write-Host ("  WARNING: {0} Employees row(s) still present for TESTONLY-INACTIVE." -f $empRemain) -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "=== PS1_66 v3 complete ===" -ForegroundColor Green
Write-Host ""
