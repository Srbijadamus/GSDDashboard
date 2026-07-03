# test_fix3.ps1
# Verifies Fix 3: GET /api/wic/open and GET /api/overview/wic-status honor
# a SickLeave record when NO ShiftEntry exists (the imported-record case).
#
# Strategy: CONSTRUCT the scenario synthetically on a throwaway future date
# (2026-08-15, Saturday -- no production data expected, cannot collide).
# All inserts go directly to SQL; cleanup removes every inserted row.
#
# Order of operations:
#   1. INSERT WicShiftEntry (IsOnSite=1, alias-mapped SupportLocation)
#   2. GET baseline /api/wic/open  => employee present: eff+1, abs+0
#   3. SQL INSERT SickLeave only (SourceSheet=FIX3_TEST, NO ShiftEntry)
#   4. Confirm no ShiftEntry exists for that date
#   5. GET /api/wic/open          => employee absent: abs+1, eff-1  [CORE]
#   6. GET /api/overview/wic-status => FullName in location.absentAgents [CORE]
#   7. GET /api/sickleave/active  => sanity
#   CLEANUP: delete WicShiftEntry + SickLeave + any ShiftEntry (expect 0)
#
# IMPORTANT: App must be rebuilt+restarted with Fix 3 before running.
# Run with: pwsh -File C:\GSDDashboard\test_fix3.ps1

$BaseUrl    = "http://localhost:5000"
$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$TestDate   = "2026-08-15"   # Saturday; clean future date, no production data expected
$TestDow    = "Saturday"     # WicShiftEntry.DayOfWeek column value

Write-Host ""
Write-Host "=== FIX 3 TEST: WicShiftService + OverviewService SickLeave Honor ===" -ForegroundColor Yellow
Write-Host "IMPORTANT: App must be rebuilt+restarted with Fix 3 before running."
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
        foreach ($kv in $Params.GetEnumerator()) { [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value) }
        return $cmd.ExecuteScalar()
    } finally { $conn.Close() }
}

function Invoke-SqlNonQuery([string]$Sql, [hashtable]$Params = @{}) {
    Add-Type -AssemblyName "System.Data"
    $conn = New-Object System.Data.SqlClient.SqlConnection($script:ConnString)
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Sql
        foreach ($kv in $Params.GetEnumerator()) { [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value) }
        return $cmd.ExecuteNonQuery()
    } finally { $conn.Close() }
}

function Invoke-SqlFirstRow([string]$Sql, [hashtable]$Params = @{}) {
    Add-Type -AssemblyName "System.Data"
    $conn = New-Object System.Data.SqlClient.SqlConnection($script:ConnString)
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Sql
        foreach ($kv in $Params.GetEnumerator()) { [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value) }
        $reader = $cmd.ExecuteReader()
        if ($reader.Read()) {
            $row = @{}
            for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                $v = $reader.GetValue($i)
                $row[$reader.GetName($i)] = if ($v -is [System.DBNull]) { $null } else { $v }
            }
            $reader.Close()
            return [PSCustomObject]$row
        }
        $reader.Close()
        return $null
    } finally { $conn.Close() }
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
# Alias VALUES fragment -- mirrors WicLocationMatcher._aliases exactly.
# Embedded directly in SQL strings so there is no runtime escaping needed.
# ---------------------------------------------------------------------------

# Pick first alias-mapped WicLocation that is IsActive=1.
Write-Host "Resolving test location (first alias-mapped active WicLocation)..." -ForegroundColor Cyan
$locSql = @"
SELECT TOP 1 wl.LocationCode, m.sl AS SupportLocKey
FROM (VALUES
    ('Essen BP1','DE_Essen_BP1'),
    ('Essen TK1','DE_Essen_TK1'),
    ('Halle','DE_Halle'),
    ('Berlin - Gaussstr','DE_Berlin_Gauss'),
    ('Furstenwalde','DE_Furstenwalde'),
    ('Munchen','DE_Munchen'),
    ('Osnabruck','DE_Osnabruck'),
    ('Saarbrucken','DE_Saarbrucken'),
    ('Demmin - Am Hanseufer','DE_Demmin_Hanse'),
    ('Denbosch','NL_Denbosch'),
    ('Augsburg','DE_Augsburg'),
    ('Bamberg','DE_Bamberg'),
    ('Brokdorf','DE_Brokdorf'),
    ('Dortmund','DE_Dortmund'),
    ('Emmerthal','DE_Emmerthal'),
    ('Essenbach','DE_Essenbach'),
    ('Grafenrheinfeld','DE_Grafenrheinfeld'),
    ('Hamburg','DE_Hamburg'),
    ('Hannover','DE_Hannover'),
    ('Helmstedt','DE_Helmstedt'),
    ('Neu-Isenburg','DE_NeuIsenburg'),
    ('Pfaffenhofen','PFAFFENHOFEN'),
    ('Potsdam','DE_Potsdam'),
    ('Quickborn','DE_Quickborn'),
    ('Regensburg','DE_Regensburg'),
    ('Rendsburg','RENDSBURG'),
    ('Salzgitter','DE_Salzgitter'),
    ('Stade','DE_Stade'),
    ('Stadland','DE_Stadland'),
    ('Zwolle','NL_Zwolle')
) AS m(sl, lc)
JOIN WicLocations wl
  ON (wl.LocationCode = m.lc OR wl.LocationCodeLegacy = m.lc)
  AND wl.IsActive = 1
ORDER BY wl.LocationCode
"@
$locRow = Invoke-SqlFirstRow -Sql $locSql
if (-not $locRow) {
    Write-Host "ERROR: No alias-mapped active WicLocation found -- cannot run test." -ForegroundColor Red
    exit 1
}
$ActualLocCode = "$($locRow.LocationCode)"
$SupportLocKey = "$($locRow.SupportLocKey)"
Write-Host ("  Using location: {0}  SupportLocation key: '{1}'" -f $ActualLocCode, $SupportLocKey) -ForegroundColor Green

# Pick an active employee with no WicShiftEntry and no SickLeave on the test date.
Write-Host "Resolving test employee (active, no data on $TestDate)..." -ForegroundColor Cyan
$empSql = @"
SELECT TOP 1
    e.EmployeeId,
    CASE WHEN e.FullName IS NULL OR e.FullName = '' THEN e.EmployeeId ELSE e.FullName END AS DisplayName
FROM Employees e
WHERE e.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM WicShiftEntries w
      WHERE w.EmployeeId = e.EmployeeId AND w.ShiftDate = @dt
  )
  AND NOT EXISTS (
      SELECT 1 FROM SickLeaves sl
      WHERE sl.EmployeeId = e.EmployeeId
        AND sl.FirstDay <= @dt AND sl.LastDay >= @dt
  )
ORDER BY e.EmployeeId
"@
$empRow = Invoke-SqlFirstRow -Sql $empSql -Params @{dt = $TestDate}
if (-not $empRow) {
    Write-Host "ERROR: No active employee free of data on $TestDate -- cannot run test." -ForegroundColor Red
    exit 1
}
$EmpId       = "$($empRow.EmployeeId)"
$EmpDisplay  = "$($empRow.DisplayName)"   # matches OverviewService: emp?.FullName ?? w.EmployeeId

Write-Host ("  Using employee: {0}  display name: '{1}'" -f $EmpId, $EmpDisplay) -ForegroundColor Green
Write-Host ("  Test date: {0} (Saturday -- future, clean)" -f $TestDate) -ForegroundColor Green
Write-Host ""

$InsertedWicId = 0
$InsertedSlId  = 0

try {

    # ------------------------------------------------------------------
    # Step 1: INSERT WicShiftEntry (IsOnSite=1, alias-mapped SupportLocation)
    # This makes the employee visible to the coverage service.
    # ------------------------------------------------------------------
    Write-Host "Step 1: SQL INSERT WicShiftEntry (IsOnSite=1, SupportLocation='$SupportLocKey')..." -ForegroundColor Cyan
    $wicInsertSql = @"
INSERT INTO WicShiftEntries
    (EmployeeId, ShiftDate, DayOfWeek, SupportLocation, IsOnSite, IsGSDDay, IsOffDay, Task)
VALUES
    (@empId, @dt, @dow, @sl, 1, 0, 0, 'WIC')
"@
    try {
        Invoke-SqlNonQuery -Sql $wicInsertSql -Params @{
            empId = $EmpId; dt = $TestDate; dow = $TestDow; sl = $SupportLocKey
        } | Out-Null
        $rawWicId = Invoke-SqlScalar -Sql "SELECT MAX(Id) FROM WicShiftEntries WHERE EmployeeId=@e AND ShiftDate=@d" -Params @{e=$EmpId; d=$TestDate}
        $InsertedWicId = if ($rawWicId -is [System.DBNull] -or $null -eq $rawWicId) { 0 } else { [int]$rawWicId }
        Mark 1 ($InsertedWicId -gt 0) "WicShiftEntry id=$InsertedWicId inserted (IsOnSite=1, '$SupportLocKey')"
    } catch {
        Mark 1 $false "SQL INSERT WicShiftEntry failed: $_"
    }

    # ------------------------------------------------------------------
    # Step 2: Baseline GET /api/wic/open BEFORE SickLeave
    # Employee has WicShiftEntry, no SickLeave, no ShiftEntry.
    # Expect: effectiveCoverage includes them (+1 vs a fresh date), absentCount = 0.
    # ------------------------------------------------------------------
    Write-Host ("Step 2: GET /api/wic/open?date={0}&horizon=1 (baseline -- employee PRESENT)..." -f $TestDate) -ForegroundColor Cyan
    $locBefore = $null
    try {
        $openBefore = Invoke-RestMethod -Uri "$BaseUrl/api/wic/open?date=$TestDate&horizon=1" -Method GET -ErrorAction Stop
        $dayBefore  = @($openBefore | Where-Object { $_.date -eq $TestDate }) | Select-Object -First 1
        if ($dayBefore -and $dayBefore.locations) {
            $locBefore = @($dayBefore.locations | Where-Object { $_.locationCode -eq $ActualLocCode }) | Select-Object -First 1
        }
        if ($locBefore) {
            Write-Host ("  Baseline {0}: scheduledCount={1}, absentCount={2}, effectiveCoverage={3}" -f `
                $ActualLocCode, $locBefore.scheduledCount, $locBefore.absentCount, $locBefore.effectiveCoverage)
            $baselineOk = ([int]$locBefore.scheduledCount -ge 1) -and ([int]$locBefore.absentCount -eq 0)
            Mark 2 $baselineOk ("scheduledCount={0} (expect >=1), absentCount={1} (expect 0)" -f $locBefore.scheduledCount, $locBefore.absentCount)
        } else {
            Mark 2 $false ("Location {0} not found in /api/wic/open response" -f $ActualLocCode)
        }
    } catch {
        Mark 2 $false "GET /api/wic/open failed: $_"
    }

    # ------------------------------------------------------------------
    # Step 3: SQL INSERT SickLeave ONLY -- no API call, no ShiftEntry created.
    # This is the "imported record" scenario Fix 3 addresses.
    # ------------------------------------------------------------------
    Write-Host ("Step 3: SQL INSERT SickLeave for {0} on {1} (bypassing API -- no ShiftEntry)..." -f $EmpId, $TestDate) -ForegroundColor Cyan
    $slInsertSql = @"
INSERT INTO SickLeaves
    (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, SourceSheet)
SELECT @empId, FirstName, LastName, TeamLeadName, @dt, @dt, 1, 'Self', 'FIX3_TEST'
FROM Employees WHERE EmployeeId = @empId
"@
    try {
        Invoke-SqlNonQuery -Sql $slInsertSql -Params @{empId = $EmpId; dt = $TestDate} | Out-Null
        $rawSlId = Invoke-SqlScalar -Sql "SELECT MAX(Id) FROM SickLeaves WHERE EmployeeId=@e AND SourceSheet='FIX3_TEST'" -Params @{e=$EmpId}
        $InsertedSlId = if ($rawSlId -is [System.DBNull] -or $null -eq $rawSlId) { 0 } else { [int]$rawSlId }
        Mark 3 ($InsertedSlId -gt 0) "SickLeave id=$InsertedSlId inserted (SourceSheet=FIX3_TEST, no ShiftEntry)"
    } catch {
        Mark 3 $false "SQL INSERT SickLeave failed: $_"
    }

    # ------------------------------------------------------------------
    # Step 4: Confirm NO ShiftEntry was created (the imported-record case)
    # ------------------------------------------------------------------
    Write-Host ("Step 4: Confirm no ShiftEntry for {0} on {1}..." -f $EmpId, $TestDate) -ForegroundColor Cyan
    try {
        $seCount = [int](Invoke-SqlScalar -Sql "SELECT COUNT(*) FROM ShiftEntries WHERE EmployeeId=@e AND ShiftDate=@d" -Params @{e=$EmpId; d=$TestDate})
        Mark 4 ($seCount -eq 0) "ShiftEntry count=$seCount on $TestDate (expect 0)"
    } catch {
        Mark 4 $false "SQL ShiftEntry check failed: $_"
    }

    # ------------------------------------------------------------------
    # Step 5: GET /api/wic/open -- CORE FIX 3 CHECK
    # Employee has WicShiftEntry (IsOnSite) + SickLeave but NO ShiftEntry.
    # Fix 3 must pick up the SickLeave via AvailabilityResolver.GetAbsentIdsAsync.
    # Expect: absentCount = baseline+1, effectiveCoverage = baseline-1.
    # ------------------------------------------------------------------
    Write-Host ("Step 5: GET /api/wic/open?date={0}&horizon=1 -- {1} absentCount must be +1 [CORE FIX 3]..." -f $TestDate, $ActualLocCode) -ForegroundColor Cyan
    $locAfter = $null
    try {
        $openAfter = Invoke-RestMethod -Uri "$BaseUrl/api/wic/open?date=$TestDate&horizon=1" -Method GET -ErrorAction Stop
        $dayAfter  = @($openAfter | Where-Object { $_.date -eq $TestDate }) | Select-Object -First 1
        if ($dayAfter -and $dayAfter.locations) {
            $locAfter = @($dayAfter.locations | Where-Object { $_.locationCode -eq $ActualLocCode }) | Select-Object -First 1
        }
        if ($locBefore -and $locAfter) {
            $absBefore = [int]$locBefore.absentCount
            $absAfter  = [int]$locAfter.absentCount
            $effBefore = [int]$locBefore.effectiveCoverage
            $effAfter  = [int]$locAfter.effectiveCoverage
            Write-Host ("  {0}: absentCount {1}->{2} (expect {3}), effectiveCoverage {4}->{5} (expect {6})" -f `
                $ActualLocCode, $absBefore, $absAfter, ($absBefore + 1), $effBefore, $effAfter, ($effBefore - 1))
            $okAbs = ($absAfter -eq ($absBefore + 1))
            $okEff = ($effAfter -eq ($effBefore - 1))
            Mark 5 ($okAbs -and $okEff) ("absentCount {0}->{1}, effectiveCoverage {2}->{3}" -f $absBefore, $absAfter, $effBefore, $effAfter)
        } elseif (-not $locBefore) {
            Mark 5 $false "No baseline (step 2 failed) -- cannot assert delta"
        } else {
            Mark 5 $false ("Location {0} not found in after-response" -f $ActualLocCode)
        }
    } catch {
        Mark 5 $false "GET /api/wic/open failed: $_"
    }

    # ------------------------------------------------------------------
    # Step 6: GET /api/overview/wic-status -- CORE FIX 3, Overview endpoint
    # OverviewService emits emp?.FullName ?? w.EmployeeId in absentAgents.
    # Assert the employee's display name appears in THAT location's list.
    # ------------------------------------------------------------------
    Write-Host ("Step 6: GET /api/overview/wic-status?date={0}&horizon=1 -- '{1}' in {2}.absentAgents [CORE FIX 3]..." -f $TestDate, $EmpDisplay, $ActualLocCode) -ForegroundColor Cyan
    try {
        $ovAfter   = Invoke-RestMethod -Uri "$BaseUrl/api/overview/wic-status?date=$TestDate&horizon=1" -Method GET -ErrorAction Stop
        $ovDay     = $null
        if ($ovAfter.days) {
            $ovDay = @($ovAfter.days | Where-Object { $_.date -eq $TestDate }) | Select-Object -First 1
        }
        $targetLoc = $null
        if ($ovDay -and $ovDay.locations) {
            $targetLoc = @($ovDay.locations | Where-Object { $_.locationCode -eq $ActualLocCode }) | Select-Object -First 1
        }
        if ($targetLoc) {
            $agents = @($targetLoc.absentAgents)
            $found  = $agents -contains $EmpDisplay
            Write-Host ("  {0} absentAgents ({1} entries): {2}" -f $ActualLocCode, $agents.Count, ($agents -join " | "))
            Mark 6 $found ("'{0}' {1} in {2}.absentAgents" -f $EmpDisplay, $(if ($found) { "FOUND" } else { "NOT FOUND" }), $ActualLocCode)
        } else {
            Mark 6 $false ("Location {0} not found in /api/overview/wic-status response" -f $ActualLocCode)
        }
    } catch {
        Mark 6 $false "GET /api/overview/wic-status failed: $_"
    }

    # ------------------------------------------------------------------
    # Step 7: /api/sickleave/active -- sanity check
    # ------------------------------------------------------------------
    Write-Host ("Step 7: GET /api/sickleave/active?date={0} (sanity check)..." -f $TestDate) -ForegroundColor Cyan
    try {
        $active = Invoke-RestMethod -Uri "$BaseUrl/api/sickleave/active?date=$TestDate" -Method GET -ErrorAction Stop
        $hit    = @($active | Where-Object { $_.employeeId -eq $EmpId })
        Mark 7 ($hit.Count -gt 0) "Employee $EmpId in /api/sickleave/active (SickLeave visible to API)"
    } catch {
        Mark 7 $false "GET /api/sickleave/active failed: $_"
    }

} finally {

    # ------------------------------------------------------------------
    # CLEANUP -- always runs; removes every row inserted by this test
    # ------------------------------------------------------------------
    Write-Host ""
    Write-Host "CLEANUP..." -ForegroundColor Cyan

    if ($InsertedSlId -gt 0) {
        try {
            Invoke-SqlNonQuery -Sql "DELETE FROM SickLeaves WHERE Id=@id" -Params @{id=$InsertedSlId} | Out-Null
            Write-Host "  Deleted SickLeave id=$InsertedSlId." -ForegroundColor DarkGreen
        } catch {
            Write-Host "  WARNING: Could not delete SickLeave id=$InsertedSlId : $_" -ForegroundColor Yellow
        }
    }

    # Belt-and-suspenders: remove any stray FIX3_TEST SickLeave rows for this employee
    try {
        $extra = Invoke-SqlNonQuery -Sql "DELETE FROM SickLeaves WHERE EmployeeId=@e AND SourceSheet='FIX3_TEST'" -Params @{e=$EmpId}
        if ($extra -gt 0) { Write-Host "  Removed $extra stray FIX3_TEST SickLeave rows." -ForegroundColor DarkGreen }
    } catch {}

    if ($InsertedWicId -gt 0) {
        try {
            Invoke-SqlNonQuery -Sql "DELETE FROM WicShiftEntries WHERE Id=@id" -Params @{id=$InsertedWicId} | Out-Null
            Write-Host "  Deleted WicShiftEntry id=$InsertedWicId." -ForegroundColor DarkGreen
        } catch {
            Write-Host "  WARNING: Could not delete WicShiftEntry id=$InsertedWicId : $_" -ForegroundColor Yellow
        }
    }

    # Belt-and-suspenders: remove any ShiftEntry that might have been created (expect 0)
    try {
        $seRemoved = Invoke-SqlNonQuery -Sql "DELETE FROM ShiftEntries WHERE EmployeeId=@e AND ShiftDate=@d" -Params @{e=$EmpId; d=$TestDate}
        if ($seRemoved -gt 0) {
            Write-Host "  WARNING: Removed $seRemoved unexpected ShiftEntry rows (Fix 3 should NOT create ShiftEntries)." -ForegroundColor Yellow
        }
    } catch {}

    # Verify DB is completely clean
    try {
        $remSl  = [int](Invoke-SqlScalar -Sql "SELECT COUNT(*) FROM SickLeaves WHERE SourceSheet='FIX3_TEST'")
        $remWic = [int](Invoke-SqlScalar -Sql "SELECT COUNT(*) FROM WicShiftEntries WHERE EmployeeId=@e AND ShiftDate=@d" -Params @{e=$EmpId; d=$TestDate})
        $remSe  = [int](Invoke-SqlScalar -Sql "SELECT COUNT(*) FROM ShiftEntries WHERE EmployeeId=@e AND ShiftDate=@d" -Params @{e=$EmpId; d=$TestDate})
        $cleanOk = ($remSl -eq 0) -and ($remWic -eq 0) -and ($remSe -eq 0)
        Write-Host ("  DB check: FIX3_TEST SickLeaves={0}, WicShiftEntries={1}, ShiftEntries={2} (all expect 0)" -f $remSl, $remWic, $remSe) -ForegroundColor $(if ($cleanOk) { "DarkGreen" } else { "Yellow" })
    } catch {}
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

Write-Host ""
if ($AllPassed) {
    Write-Host "FIX 3 TEST: PASSED" -ForegroundColor Green
} else {
    Write-Host "FIX 3 TEST: FAILED (step $FirstFail)" -ForegroundColor Red
}
Write-Host ""
