# test_fix1_sickleave.ps1
# Verifies Fix 1: SickLeave PATCH now reverts stale SL ShiftEntries and syncs new ones.
#
# IMPORTANT: The app MUST be rebuilt and restarted after Fix 1 before running this test.
# If that has not been done yet, run PS1_19_FinalBuildVerify.ps1 first.
#
# Run with: pwsh -File C:\GSDDashboard\test_fix1_sickleave.ps1

$BaseUrl    = "http://localhost:5000"
$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Write-Host ""
Write-Host "=== FIX 1 TEST: SickLeave Patch ShiftSync ===" -ForegroundColor Yellow
Write-Host ""
Write-Host "IMPORTANT: App must be rebuilt+restarted with Fix 1 before running this test."
Write-Host "If not done yet, run: C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1"
Write-Host ""

# ---------------------------------------------------------------------------
# SQL helpers
# ---------------------------------------------------------------------------

function Invoke-SqlScalar([string]$Sql, [hashtable]$Params = @{}) {
    Add-Type -AssemblyName "System.Data"
    $conn = New-Object System.Data.SqlClient.SqlConnection($script:ConnString)
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Sql
        foreach ($kv in $Params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        return $cmd.ExecuteScalar()
    } finally {
        $conn.Close()
    }
}

function Invoke-SqlNonQuery([string]$Sql, [hashtable]$Params = @{}) {
    Add-Type -AssemblyName "System.Data"
    $conn = New-Object System.Data.SqlClient.SqlConnection($script:ConnString)
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Sql
        foreach ($kv in $Params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        return $cmd.ExecuteNonQuery()
    } finally {
        $conn.Close()
    }
}

# ---------------------------------------------------------------------------
# Step tracking
# ---------------------------------------------------------------------------

$AllPassed = $true
$FirstFail = 0

function Mark([int]$StepNum, [bool]$Pass, [string]$Msg) {
    if ($Pass) {
        Write-Host ("  Step {0} PASS: {1}" -f $StepNum, $Msg) -ForegroundColor Green
    } else {
        Write-Host ("  Step {0} FAIL: {1}" -f $StepNum, $Msg) -ForegroundColor Red
        $script:AllPassed = $false
        if ($script:FirstFail -eq 0) { $script:FirstFail = $StepNum }
    }
}

# ---------------------------------------------------------------------------
# Pick a safe test employee (one with no SL in Jul 7-11 2026)
# ---------------------------------------------------------------------------

Write-Host "Picking test employee (no existing SL in 2026-07-07..2026-07-11)..." -ForegroundColor Cyan

$pickSql = @"
SELECT TOP 1 EmployeeId
FROM   Employees
WHERE  EmployeeId IS NOT NULL
  AND  EmployeeId NOT IN (
           SELECT ISNULL(EmployeeId, '')
           FROM   SickLeaves
           WHERE  FirstDay <= '2026-07-11'
             AND  LastDay  >= '2026-07-07'
       )
ORDER BY EmployeeId
"@

$EmpId = Invoke-SqlScalar -Sql $pickSql

if (-not $EmpId) {
    Write-Host "ERROR: No safe test employee found (all employees have SL in test window)." -ForegroundColor Red
    exit 1
}
Write-Host "  Chose employee: $EmpId" -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# SQL queries reused across steps
# ---------------------------------------------------------------------------

$sqlCountSl10 = "SELECT COUNT(*) FROM ShiftEntries WHERE EmployeeId=@e AND ShiftDate='2026-07-10' AND SourceModule='SickLeave' AND ShiftType='SL'"
$sqlCleanup   = "DELETE FROM ShiftEntries WHERE EmployeeId=@e AND ShiftDate>='2026-07-07' AND ShiftDate<='2026-07-11' AND SourceModule='SickLeave'"
$sqlVerify    = "SELECT COUNT(*) FROM ShiftEntries WHERE EmployeeId=@e AND ShiftDate>='2026-07-07' AND ShiftDate<='2026-07-11' AND SourceModule='SickLeave'"

# ---------------------------------------------------------------------------
# Test body
# ---------------------------------------------------------------------------

$SlId = 0

try {

    # ------------------------------------------------------------------
    # Step 1: CREATE sick leave Jul 7-11
    # ------------------------------------------------------------------
    Write-Host "Step 1: POST /api/sickleave (Jul 7-11, type=Self)..." -ForegroundColor Cyan
    try {
        $body = ConvertTo-Json @{
            employeeId = $EmpId
            startDate  = "2026-07-07"
            endDate    = "2026-07-11"
            type       = "Self"
        }
        $created = Invoke-RestMethod -Uri "$BaseUrl/api/sickleave" -Method POST `
                       -ContentType "application/json" -Body $body -ErrorAction Stop
        $rawId = $created.id
        if ($rawId) { $SlId = [int]$rawId } else { $SlId = 0 }
        Mark 1 ($SlId -gt 0) "SickLeave created, id=$SlId"
    } catch {
        Mark 1 $false "POST /api/sickleave threw: $_"
    }

    # ------------------------------------------------------------------
    # Steps 2-7 only make sense if the record was created
    # ------------------------------------------------------------------
    if ($SlId -gt 0) {

        # Step 2: Employee appears in /active on Jul 7
        Write-Host "Step 2: GET /api/sickleave/active?date=2026-07-07 (expect present)..." -ForegroundColor Cyan
        try {
            $list = Invoke-RestMethod -Uri "$BaseUrl/api/sickleave/active?date=2026-07-07" `
                        -Method GET -ErrorAction Stop
            $hits = @($list | Where-Object { $_.employeeId -eq $EmpId })
            Mark 2 ($hits.Count -gt 0) "Employee in /active on Jul 7: $($hits.Count -gt 0)"
        } catch {
            Mark 2 $false "GET /active?date=2026-07-07 threw: $_"
        }

        # Step 3: SL ShiftEntry present in DB for Jul 10
        Write-Host "Step 3: SQL - ShiftEntry for Jul 10 exists (expect present)..." -ForegroundColor Cyan
        try {
            $n = [int](Invoke-SqlScalar -Sql $sqlCountSl10 -Params @{ e = $EmpId })
            Mark 3 ($n -gt 0) "ShiftEntry Jul 10 count=$n (expect >0)"
        } catch {
            Mark 3 $false "SQL check threw: $_"
        }

        # Step 4: PATCH endDate to Jul 9
        Write-Host "Step 4: PATCH /api/sickleave/$SlId -> endDate=2026-07-09..." -ForegroundColor Cyan
        try {
            $patched = Invoke-RestMethod -Uri "$BaseUrl/api/sickleave/$SlId" -Method PATCH `
                           -ContentType "application/json" -Body '{"endDate":"2026-07-09"}' -ErrorAction Stop
            $newEnd = "$($patched.lastDay)"
            Mark 4 ($newEnd -like "2026-07-09*") "lastDay after patch = $newEnd"
        } catch {
            Mark 4 $false "PATCH threw: $_"
        }

        # Step 5: Employee NOT in /active on Jul 10  *** CORE FIX 1 CHECK ***
        Write-Host "Step 5: GET /api/sickleave/active?date=2026-07-10 (expect ABSENT -- core Fix 1)..." -ForegroundColor Cyan
        try {
            $list = Invoke-RestMethod -Uri "$BaseUrl/api/sickleave/active?date=2026-07-10" `
                        -Method GET -ErrorAction Stop
            $hits = @($list | Where-Object { $_.employeeId -eq $EmpId })
            Mark 5 ($hits.Count -eq 0) "Employee absent on Jul 10: $($hits.Count -eq 0)"
        } catch {
            Mark 5 $false "GET /active?date=2026-07-10 threw: $_"
        }

        # Step 6: SL ShiftEntry GONE from DB for Jul 10
        Write-Host "Step 6: SQL - ShiftEntry for Jul 10 after patch (expect GONE)..." -ForegroundColor Cyan
        try {
            $n = [int](Invoke-SqlScalar -Sql $sqlCountSl10 -Params @{ e = $EmpId })
            Mark 6 ($n -eq 0) "ShiftEntry Jul 10 after patch count=$n (expect 0)"
        } catch {
            Mark 6 $false "SQL check threw: $_"
        }

        # Step 7: Employee STILL in /active on Jul 7
        Write-Host "Step 7: GET /api/sickleave/active?date=2026-07-07 (expect still present)..." -ForegroundColor Cyan
        try {
            $list = Invoke-RestMethod -Uri "$BaseUrl/api/sickleave/active?date=2026-07-07" `
                        -Method GET -ErrorAction Stop
            $hits = @($list | Where-Object { $_.employeeId -eq $EmpId })
            Mark 7 ($hits.Count -gt 0) "Employee still present on Jul 7: $($hits.Count -gt 0)"
        } catch {
            Mark 7 $false "GET /active?date=2026-07-07 threw: $_"
        }

    } else {
        Write-Host "  Steps 2-7 skipped: step 1 failed, no SickLeave record to test against." -ForegroundColor Yellow
    }

} finally {

    # ------------------------------------------------------------------
    # Step 8: CLEANUP (always runs, even on error)
    # ------------------------------------------------------------------
    Write-Host ""
    Write-Host "Step 8: CLEANUP..." -ForegroundColor Cyan

    # Delete the test SickLeave via API
    if ($SlId -gt 0) {
        try {
            Invoke-RestMethod -Uri "$BaseUrl/api/sickleave/$SlId" -Method DELETE -ErrorAction Stop | Out-Null
            Write-Host "  Deleted SickLeave id=$SlId via DELETE /api/sickleave/$SlId." -ForegroundColor DarkGreen
        } catch {
            Write-Host "  WARNING: API delete for SickLeave id=$SlId failed: $_" -ForegroundColor Yellow
        }
    }

    # Belt-and-suspenders: remove any SL ShiftEntries we left behind
    try {
        $deleted = Invoke-SqlNonQuery -Sql $sqlCleanup -Params @{ e = $EmpId }
        Write-Host "  SQL removed $deleted residual SL ShiftEntry rows for $EmpId in Jul 7-11." -ForegroundColor DarkGreen
    } catch {
        Write-Host "  WARNING: ShiftEntry SQL cleanup failed: $_" -ForegroundColor Yellow
    }

    # Verify DB is clean
    try {
        $rem = [int](Invoke-SqlScalar -Sql $sqlVerify -Params @{ e = $EmpId })
        Mark 8 ($rem -eq 0) "DB clean: $rem SL ShiftEntries remain for $EmpId in Jul 7-11 (expect 0)"
    } catch {
        Mark 8 $false "Cleanup verification SQL threw: $_"
    }
}

# ---------------------------------------------------------------------------
# Final summary
# ---------------------------------------------------------------------------

Write-Host ""
if ($AllPassed) {
    Write-Host "FIX 1 TEST: PASSED" -ForegroundColor Green
} else {
    Write-Host "FIX 1 TEST: FAILED (step $FirstFail)" -ForegroundColor Red
}
Write-Host ""
