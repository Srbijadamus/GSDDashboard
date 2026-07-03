# teresa_phase2_delete.ps1
# PHASE 2: Permanently delete Teresa Kwasniewska (EmployeeId=9074584).
# Single transaction with XACT_ABORT ON. Any count mismatch triggers ROLLBACK.
# After commit, verifies all 17 table/columns are zero.
#
# Run with: pwsh -File C:\GSDDashboard\teresa_phase2_delete.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$EmpId      = "9074584"
$EmpName    = "Teresa Kwasniewska"

Add-Type -AssemblyName "System.Data"

Write-Host ""
Write-Host "=== PHASE 2: DELETE Teresa Kwasniewska (EmployeeId=$EmpId) ===" -ForegroundColor Red
Write-Host "Transaction + XACT_ABORT ON. Any count mismatch rolls back everything." -ForegroundColor Yellow
Write-Host ""
Write-Host "Expected per table: ShiftEntries=181, SickLeaves=2, Vacations=3," -ForegroundColor Yellow
Write-Host "                    ALBalance=1, BreakSlots=2, Employees=1  (total 190)" -ForegroundColor Yellow
Write-Host ""

# ---------------------------------------------------------------------------
# Open connection and set XACT_ABORT ON before beginning the transaction
# ---------------------------------------------------------------------------
$Conn = New-Object System.Data.SqlClient.SqlConnection($ConnString)
$Conn.Open()

$xactCmd = $Conn.CreateCommand()
$xactCmd.CommandText = "SET XACT_ABORT ON"
[void]$xactCmd.ExecuteNonQuery()

$Txn = $Conn.BeginTransaction()
Write-Host "BEGIN TRANSACTION" -ForegroundColor Cyan
Write-Host ""

$TotalDeleted = 0
$DeleteOk     = $false

try {

    # ------------------------------------------------------------------
    # ShiftEntries  (expect 181)
    # ------------------------------------------------------------------
    $cmd = $Conn.CreateCommand()
    $cmd.Transaction = $Txn
    $cmd.CommandText = "DELETE FROM ShiftEntries WHERE EmployeeId = @id"
    [void]$cmd.Parameters.AddWithValue("@id", $EmpId)
    $nSE = $cmd.ExecuteNonQuery()
    $colSE = if ($nSE -eq 181) { "Green" } else { "Red" }
    Write-Host ("  ShiftEntries   deleted: {0,5}  (expect 181)" -f $nSE) -ForegroundColor $colSE
    if ($nSE -ne 181) { throw ("ShiftEntries count mismatch: got {0}, expected 181" -f $nSE) }
    $TotalDeleted += $nSE

    # ------------------------------------------------------------------
    # SickLeaves  (expect 2)
    # ------------------------------------------------------------------
    $cmd = $Conn.CreateCommand()
    $cmd.Transaction = $Txn
    $cmd.CommandText = "DELETE FROM SickLeaves WHERE EmployeeId = @id"
    [void]$cmd.Parameters.AddWithValue("@id", $EmpId)
    $nSL = $cmd.ExecuteNonQuery()
    $colSL = if ($nSL -eq 2) { "Green" } else { "Red" }
    Write-Host ("  SickLeaves     deleted: {0,5}  (expect 2)" -f $nSL) -ForegroundColor $colSL
    if ($nSL -ne 2) { throw ("SickLeaves count mismatch: got {0}, expected 2" -f $nSL) }
    $TotalDeleted += $nSL

    # ------------------------------------------------------------------
    # Vacations  (expect 3)
    # ------------------------------------------------------------------
    $cmd = $Conn.CreateCommand()
    $cmd.Transaction = $Txn
    $cmd.CommandText = "DELETE FROM Vacations WHERE EmployeeId = @id"
    [void]$cmd.Parameters.AddWithValue("@id", $EmpId)
    $nVac = $cmd.ExecuteNonQuery()
    $colVac = if ($nVac -eq 3) { "Green" } else { "Red" }
    Write-Host ("  Vacations      deleted: {0,5}  (expect 3)" -f $nVac) -ForegroundColor $colVac
    if ($nVac -ne 3) { throw ("Vacations count mismatch: got {0}, expected 3" -f $nVac) }
    $TotalDeleted += $nVac

    # ------------------------------------------------------------------
    # ALBalance  (expect 1)
    # ------------------------------------------------------------------
    $cmd = $Conn.CreateCommand()
    $cmd.Transaction = $Txn
    $cmd.CommandText = "DELETE FROM ALBalance WHERE EmployeeId = @id"
    [void]$cmd.Parameters.AddWithValue("@id", $EmpId)
    $nAL = $cmd.ExecuteNonQuery()
    $colAL = if ($nAL -eq 1) { "Green" } else { "Red" }
    Write-Host ("  ALBalance      deleted: {0,5}  (expect 1)" -f $nAL) -ForegroundColor $colAL
    if ($nAL -ne 1) { throw ("ALBalance count mismatch: got {0}, expected 1" -f $nAL) }
    $TotalDeleted += $nAL

    # ------------------------------------------------------------------
    # BreakSlots  (expect 2)
    # ------------------------------------------------------------------
    $cmd = $Conn.CreateCommand()
    $cmd.Transaction = $Txn
    $cmd.CommandText = "DELETE FROM BreakSlots WHERE EmployeeId = @id"
    [void]$cmd.Parameters.AddWithValue("@id", $EmpId)
    $nBS = $cmd.ExecuteNonQuery()
    $colBS = if ($nBS -eq 2) { "Green" } else { "Red" }
    Write-Host ("  BreakSlots     deleted: {0,5}  (expect 2)" -f $nBS) -ForegroundColor $colBS
    if ($nBS -ne 2) { throw ("BreakSlots count mismatch: got {0}, expected 2" -f $nBS) }
    $TotalDeleted += $nBS

    # ------------------------------------------------------------------
    # Employees  (expect 1) -- LAST, after all dependents are gone
    # ------------------------------------------------------------------
    $cmd = $Conn.CreateCommand()
    $cmd.Transaction = $Txn
    $cmd.CommandText = "DELETE FROM Employees WHERE EmployeeId = @id"
    [void]$cmd.Parameters.AddWithValue("@id", $EmpId)
    $nEmp = $cmd.ExecuteNonQuery()
    $colEmp = if ($nEmp -eq 1) { "Green" } else { "Red" }
    Write-Host ("  Employees      deleted: {0,5}  (expect 1)" -f $nEmp) -ForegroundColor $colEmp
    if ($nEmp -ne 1) { throw ("Employees count mismatch: got {0}, expected 1" -f $nEmp) }
    $TotalDeleted += $nEmp

    # ------------------------------------------------------------------
    # All counts matched -- COMMIT
    # ------------------------------------------------------------------
    $Txn.Commit()
    $DeleteOk = $true
    Write-Host ""
    Write-Host ("COMMIT -- {0} total rows deleted (expected 190)." -f $TotalDeleted) -ForegroundColor Green

} catch {
    Write-Host ""
    Write-Host ("ERROR: {0}" -f $_) -ForegroundColor Red
    try { $Txn.Rollback() } catch { }
    Write-Host "ROLLBACK executed. No rows were committed." -ForegroundColor Yellow
    $Conn.Close()
    exit 1
}

$Conn.Close()

if (-not $DeleteOk) { exit 1 }

# ---------------------------------------------------------------------------
# Verification: open a fresh connection and confirm 0 rows remain everywhere
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Verification: checking all 17 table/columns for remaining rows ..." -ForegroundColor Cyan
Write-Host ""

$Conn2 = New-Object System.Data.SqlClient.SqlConnection($ConnString)
$Conn2.Open()

function Verify-Count([string]$Label, [string]$Sql, [hashtable]$Params) {
    $cmd = $script:Conn2.CreateCommand()
    $cmd.CommandText = $Sql
    foreach ($kv in $Params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    $raw = $cmd.ExecuteScalar()
    $n = if ($raw -is [System.DBNull] -or $null -eq $raw) { 0 } else { [int]$raw }
    $color = if ($n -eq 0) { "Green" } else { "Red" }
    Write-Host ("  {0,-42} {1}" -f $Label, $n) -ForegroundColor $color
    return $n
}

$ById   = @{id = $EmpId}
$ByName = @{n  = $EmpName}
$ByPat  = @{pat = "%$EmpId%"}

$allZero = $true
$checks  = @()

$checks += Verify-Count "Employees (by ID)"                 "SELECT COUNT(*) FROM Employees           WHERE EmployeeId        = @id"                    $ById
$checks += Verify-Count "ShiftEntries (by ID)"              "SELECT COUNT(*) FROM ShiftEntries        WHERE EmployeeId        = @id"                    $ById
$checks += Verify-Count "WicShiftEntries (by ID)"           "SELECT COUNT(*) FROM WicShiftEntries     WHERE EmployeeId        = @id"                    $ById
$checks += Verify-Count "SickLeaves (by ID)"                "SELECT COUNT(*) FROM SickLeaves          WHERE EmployeeId        = @id"                    $ById
$checks += Verify-Count "Vacations (by ID)"                 "SELECT COUNT(*) FROM Vacations           WHERE EmployeeId        = @id"                    $ById
$checks += Verify-Count "ALBalance (by ID)"                 "SELECT COUNT(*) FROM ALBalance           WHERE EmployeeId        = @id"                    $ById
$checks += Verify-Count "SubstitutionHistory (by ID)"       "SELECT COUNT(*) FROM SubstitutionHistory WHERE EmployeeId        = @id"                    $ById
$checks += Verify-Count "BreakSlots (by ID)"                "SELECT COUNT(*) FROM BreakSlots          WHERE EmployeeId        = @id"                    $ById
$checks += Verify-Count "VwicRotationSlots (by ID)"         "SELECT COUNT(*) FROM VwicRotationSlots   WHERE EmployeeId        = @id"                    $ById
$checks += Verify-Count "WicAgentAssignments (by name)"     "SELECT COUNT(*) FROM WicAgentAssignments WHERE EmployeeName      = @n"                     $ByName
$checks += Verify-Count "AgentReachableCities (by ID)"      "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeId       = @id"                   $ById
$checks += Verify-Count "AgentReachableCities (by name)"    "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName     = @n"                    $ByName
$checks += Verify-Count "DailyAttendance (by ID)"           "SELECT COUNT(*) FROM DailyAttendance     WHERE AssignedEmployeeId = @id"                   $ById
$checks += Verify-Count "WicPipeline PrimaryAgent (by name)" "SELECT COUNT(*) FROM WicPipeline        WHERE PrimaryAgent      = @n"                    $ByName
$checks += Verify-Count "WicPipeline BackupAgent (by name)" "SELECT COUNT(*) FROM WicPipeline         WHERE BackupAgent       = @n"                    $ByName
$checks += Verify-Count "TrainingSchedule AgentIds (by ID)" "SELECT COUNT(*) FROM TrainingSchedule    WHERE AgentIds LIKE @pat"                         $ByPat
$checks += Verify-Count "TrainingSchedule SuggestBy (name)" "SELECT COUNT(*) FROM TrainingSchedule    WHERE SuggestedBy = @n OR ConfirmedBy = @n"       $ByName

$Conn2.Close()

$remaining = ($checks | Measure-Object -Sum).Sum
Write-Host ""
if ($remaining -eq 0) {
    Write-Host "Verification PASSED: 0 rows found across all 17 checks." -ForegroundColor Green
} else {
    Write-Host ("Verification FAILED: {0} row(s) still present -- investigate before rebuilding." -f $remaining) -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# Done -- instruct user to rebuild and verify substitute panel
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Rebuild + restart:  pwsh -File C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1"
Write-Host "  2. Open the Find Substitute panel for Stadland, date 2026-07-09."
Write-Host "  3. Confirm Teresa Kwasniewska no longer appears in the candidate list."
Write-Host ""
