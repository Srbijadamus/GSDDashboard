# batch_phase2_delete.ps1
# PHASE 2: Permanently delete Dennis Obazee (9122677) and
#          Mohamed Khaled Mahmoud (9124147).
#
# Single transaction with XACT_ABORT ON.
# Children deleted first (by EmployeeId), WicAgentAssignments by exact name,
# Employees rows last. Any count mismatch triggers automatic ROLLBACK.
# After COMMIT: 34-check verification (17 per employee) asserts all counts = 0.
#
# Run with: pwsh -File C:\GSDDashboard\batch_phase2_delete.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Add-Type -AssemblyName "System.Data"

$DennisId    = "9122677"
$DennisName  = "Dennis Obazee"

$MohamedId   = "9124147"
$MohamedName = "Mohamed Khaled Mahmoud"

# Expected row counts locked from Phase 1 (any mismatch => ROLLBACK)
#   Dennis:  ShiftEntries=184  WicShiftEntries=211  SickLeaves=7  ALBalance=1  Employees=1
#   Mohamed: ShiftEntries=100  WicShiftEntries=364  SickLeaves=4  Vacations=4
#            ALBalance=1  WicAgentAssignments=1  Employees=1
#   Grand total: 879 rows

Write-Host ""
Write-Host "=== PHASE 2: PERMANENT DELETE - 2 EMPLOYEES ===" -ForegroundColor Red
Write-Host "  Dennis Obazee          9122677  -  404 rows"     -ForegroundColor Yellow
Write-Host "  Mohamed Khaled Mahmoud 9124147  -  475 rows  (MAIN at DE_NeuIsenburg)" -ForegroundColor Yellow
Write-Host ""
Write-Host "  SET XACT_ABORT ON. Any count mismatch => ROLLBACK, nothing committed." -ForegroundColor Yellow
Write-Host ""

# ---------------------------------------------------------------------------
# Open connection, set XACT_ABORT, begin transaction
# ---------------------------------------------------------------------------

$Conn = New-Object System.Data.SqlClient.SqlConnection($ConnString)
$Conn.Open()

$xact = $Conn.CreateCommand()
$xact.CommandText = "SET XACT_ABORT ON"
[void]$xact.ExecuteNonQuery()

$Txn = $Conn.BeginTransaction()
Write-Host "BEGIN TRANSACTION" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# Helper: execute one DELETE inside the active transaction, assert exact count.
# Throws on mismatch -- propagates to the catch block which rolls back.
# PS 5.1: no 'return if'; assign conditional to variable before returning.
# ---------------------------------------------------------------------------

function Delete-Check([string]$Label, [string]$Sql, [hashtable]$Params, [int]$Expect) {
    $cmd = $script:Conn.CreateCommand()
    $cmd.Transaction = $script:Txn
    $cmd.CommandText = $Sql
    foreach ($kv in $Params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    $n     = $cmd.ExecuteNonQuery()
    $ok    = ($n -eq $Expect)
    $color = if ($ok) { "Green" } else { "Red" }
    Write-Host ("  {0,-54} deleted={1,5}  expect={2,5}" -f $Label, $n, $Expect) -ForegroundColor $color
    if (-not $ok) {
        throw ("MISMATCH [{0}]: deleted {1}, expected {2}" -f $Label, $n, $Expect)
    }
}

$CommitOk = $false

try {

    # ------------------------------------------------------------------
    # Dennis Obazee (9122677) - children first
    # ------------------------------------------------------------------
    Write-Host "Dennis Obazee (9122677):" -ForegroundColor Cyan

    Delete-Check "Dennis / ShiftEntries"         "DELETE FROM ShiftEntries        WHERE EmployeeId         = @id" @{id=$DennisId}   184
    Delete-Check "Dennis / WicShiftEntries"      "DELETE FROM WicShiftEntries     WHERE EmployeeId         = @id" @{id=$DennisId}   211
    Delete-Check "Dennis / SickLeaves"           "DELETE FROM SickLeaves          WHERE EmployeeId         = @id" @{id=$DennisId}     7
    Delete-Check "Dennis / Vacations"            "DELETE FROM Vacations           WHERE EmployeeId         = @id" @{id=$DennisId}     0
    Delete-Check "Dennis / ALBalance"            "DELETE FROM ALBalance           WHERE EmployeeId         = @id" @{id=$DennisId}     1
    Delete-Check "Dennis / BreakSlots"           "DELETE FROM BreakSlots          WHERE EmployeeId         = @id" @{id=$DennisId}     0
    Delete-Check "Dennis / VwicRotationSlots"    "DELETE FROM VwicRotationSlots   WHERE EmployeeId         = @id" @{id=$DennisId}     0
    Delete-Check "Dennis / SubstitutionHistory"  "DELETE FROM SubstitutionHistory WHERE EmployeeId         = @id" @{id=$DennisId}     0
    Delete-Check "Dennis / DailyAttendance"      "DELETE FROM DailyAttendance     WHERE AssignedEmployeeId = @id" @{id=$DennisId}     0
    Delete-Check "Dennis / WicAgentAssignments"  "DELETE FROM WicAgentAssignments WHERE EmployeeName       = @n"  @{n=$DennisName}    0

    Write-Host ""

    # ------------------------------------------------------------------
    # Mohamed Khaled Mahmoud (9124147) - children first
    # ------------------------------------------------------------------
    Write-Host "Mohamed Khaled Mahmoud (9124147):" -ForegroundColor Cyan

    Delete-Check "Mohamed / ShiftEntries"        "DELETE FROM ShiftEntries        WHERE EmployeeId         = @id" @{id=$MohamedId}   100
    Delete-Check "Mohamed / WicShiftEntries"     "DELETE FROM WicShiftEntries     WHERE EmployeeId         = @id" @{id=$MohamedId}   364
    Delete-Check "Mohamed / SickLeaves"          "DELETE FROM SickLeaves          WHERE EmployeeId         = @id" @{id=$MohamedId}     4
    Delete-Check "Mohamed / Vacations"           "DELETE FROM Vacations           WHERE EmployeeId         = @id" @{id=$MohamedId}     4
    Delete-Check "Mohamed / ALBalance"           "DELETE FROM ALBalance           WHERE EmployeeId         = @id" @{id=$MohamedId}     1
    Delete-Check "Mohamed / BreakSlots"          "DELETE FROM BreakSlots          WHERE EmployeeId         = @id" @{id=$MohamedId}     0
    Delete-Check "Mohamed / VwicRotationSlots"   "DELETE FROM VwicRotationSlots   WHERE EmployeeId         = @id" @{id=$MohamedId}     0
    Delete-Check "Mohamed / SubstitutionHistory" "DELETE FROM SubstitutionHistory WHERE EmployeeId         = @id" @{id=$MohamedId}     0
    Delete-Check "Mohamed / DailyAttendance"     "DELETE FROM DailyAttendance     WHERE AssignedEmployeeId = @id" @{id=$MohamedId}     0
    Delete-Check "Mohamed / WicAgentAssignments" "DELETE FROM WicAgentAssignments WHERE EmployeeName       = @n"  @{n=$MohamedName}    1

    Write-Host ""

    # ------------------------------------------------------------------
    # Employees rows - LAST, after all dependents removed
    # ------------------------------------------------------------------
    Write-Host "Employees rows (both) - LAST:" -ForegroundColor Cyan

    Delete-Check "Dennis  / Employees"           "DELETE FROM Employees WHERE EmployeeId = @id" @{id=$DennisId}   1
    Delete-Check "Mohamed / Employees"           "DELETE FROM Employees WHERE EmployeeId = @id" @{id=$MohamedId}  1

    Write-Host ""

    # ------------------------------------------------------------------
    # All counts matched - COMMIT
    # ------------------------------------------------------------------
    $Txn.Commit()
    $CommitOk = $true

    $totalDeleted = 184+211+7+0+1+0+0+0+0+0 + 100+364+4+4+1+0+0+0+0+1 + 1+1
    Write-Host ("COMMIT -- {0} rows deleted (expected 879)." -f $totalDeleted) -ForegroundColor Green

} catch {
    Write-Host ""
    Write-Host ("ERROR: {0}" -f $_) -ForegroundColor Red
    try { $Txn.Rollback() } catch { }
    Write-Host "ROLLBACK executed. No rows were committed." -ForegroundColor Yellow
    $Conn.Close()
    exit 1
}

$Conn.Close()
if (-not $CommitOk) { exit 1 }

# ---------------------------------------------------------------------------
# Post-commit verification: fresh connection, 17 checks per employee = 34 total.
# Every count must be 0.
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Post-commit verification (34 checks) ..." -ForegroundColor Cyan
Write-Host ""

$Conn2 = New-Object System.Data.SqlClient.SqlConnection($ConnString)
$Conn2.Open()

function Verify-Zero([string]$Label, [string]$Sql, [hashtable]$Params) {
    $cmd = $script:Conn2.CreateCommand()
    $cmd.CommandText = $Sql
    foreach ($kv in $Params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    $raw   = $cmd.ExecuteScalar()
    $n     = if ($raw -is [System.DBNull] -or $null -eq $raw) { 0 } else { [int]$raw }
    $color = if ($n -eq 0) { "Green" } else { "Red" }
    Write-Host ("  {0,-58} {1}" -f $Label, $n) -ForegroundColor $color
    return $n
}

$checks = @()

Write-Host "  Dennis Obazee (9122677):" -ForegroundColor Cyan
$checks += Verify-Zero "    Employees"                  "SELECT COUNT(*) FROM Employees            WHERE EmployeeId         = @id" @{id=$DennisId}
$checks += Verify-Zero "    ShiftEntries"               "SELECT COUNT(*) FROM ShiftEntries         WHERE EmployeeId         = @id" @{id=$DennisId}
$checks += Verify-Zero "    WicShiftEntries"            "SELECT COUNT(*) FROM WicShiftEntries      WHERE EmployeeId         = @id" @{id=$DennisId}
$checks += Verify-Zero "    SickLeaves"                 "SELECT COUNT(*) FROM SickLeaves           WHERE EmployeeId         = @id" @{id=$DennisId}
$checks += Verify-Zero "    Vacations"                  "SELECT COUNT(*) FROM Vacations            WHERE EmployeeId         = @id" @{id=$DennisId}
$checks += Verify-Zero "    ALBalance"                  "SELECT COUNT(*) FROM ALBalance            WHERE EmployeeId         = @id" @{id=$DennisId}
$checks += Verify-Zero "    SubstitutionHistory"        "SELECT COUNT(*) FROM SubstitutionHistory  WHERE EmployeeId         = @id" @{id=$DennisId}
$checks += Verify-Zero "    BreakSlots"                 "SELECT COUNT(*) FROM BreakSlots           WHERE EmployeeId         = @id" @{id=$DennisId}
$checks += Verify-Zero "    VwicRotationSlots"          "SELECT COUNT(*) FROM VwicRotationSlots    WHERE EmployeeId         = @id" @{id=$DennisId}
$checks += Verify-Zero "    WicAgentAssignments(name)"  "SELECT COUNT(*) FROM WicAgentAssignments  WHERE EmployeeName       = @n"  @{n=$DennisName}
$checks += Verify-Zero "    AgentReachableCities(id)"   "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeId        = @id" @{id=$DennisId}
$checks += Verify-Zero "    AgentReachableCities(name)" "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName      = @n"  @{n=$DennisName}
$checks += Verify-Zero "    DailyAttendance"            "SELECT COUNT(*) FROM DailyAttendance      WHERE AssignedEmployeeId = @id" @{id=$DennisId}
$checks += Verify-Zero "    WicPipeline Primary"        "SELECT COUNT(*) FROM WicPipeline          WHERE PrimaryAgent       = @n"  @{n=$DennisName}
$checks += Verify-Zero "    WicPipeline Backup"         "SELECT COUNT(*) FROM WicPipeline          WHERE BackupAgent        = @n"  @{n=$DennisName}
$checks += Verify-Zero "    TrainingSchedule AgentIds"  "SELECT COUNT(*) FROM TrainingSchedule     WHERE AgentIds LIKE @pat"       @{pat="%$DennisId%"}
$checks += Verify-Zero "    TrainingSchedule SuggestBy" "SELECT COUNT(*) FROM TrainingSchedule     WHERE SuggestedBy = @n OR ConfirmedBy = @n" @{n=$DennisName}

Write-Host ""
Write-Host "  Mohamed Khaled Mahmoud (9124147):" -ForegroundColor Cyan
$checks += Verify-Zero "    Employees"                  "SELECT COUNT(*) FROM Employees            WHERE EmployeeId         = @id" @{id=$MohamedId}
$checks += Verify-Zero "    ShiftEntries"               "SELECT COUNT(*) FROM ShiftEntries         WHERE EmployeeId         = @id" @{id=$MohamedId}
$checks += Verify-Zero "    WicShiftEntries"            "SELECT COUNT(*) FROM WicShiftEntries      WHERE EmployeeId         = @id" @{id=$MohamedId}
$checks += Verify-Zero "    SickLeaves"                 "SELECT COUNT(*) FROM SickLeaves           WHERE EmployeeId         = @id" @{id=$MohamedId}
$checks += Verify-Zero "    Vacations"                  "SELECT COUNT(*) FROM Vacations            WHERE EmployeeId         = @id" @{id=$MohamedId}
$checks += Verify-Zero "    ALBalance"                  "SELECT COUNT(*) FROM ALBalance            WHERE EmployeeId         = @id" @{id=$MohamedId}
$checks += Verify-Zero "    SubstitutionHistory"        "SELECT COUNT(*) FROM SubstitutionHistory  WHERE EmployeeId         = @id" @{id=$MohamedId}
$checks += Verify-Zero "    BreakSlots"                 "SELECT COUNT(*) FROM BreakSlots           WHERE EmployeeId         = @id" @{id=$MohamedId}
$checks += Verify-Zero "    VwicRotationSlots"          "SELECT COUNT(*) FROM VwicRotationSlots    WHERE EmployeeId         = @id" @{id=$MohamedId}
$checks += Verify-Zero "    WicAgentAssignments(name)"  "SELECT COUNT(*) FROM WicAgentAssignments  WHERE EmployeeName       = @n"  @{n=$MohamedName}
$checks += Verify-Zero "    AgentReachableCities(id)"   "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeId        = @id" @{id=$MohamedId}
$checks += Verify-Zero "    AgentReachableCities(name)" "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName      = @n"  @{n=$MohamedName}
$checks += Verify-Zero "    DailyAttendance"            "SELECT COUNT(*) FROM DailyAttendance      WHERE AssignedEmployeeId = @id" @{id=$MohamedId}
$checks += Verify-Zero "    WicPipeline Primary"        "SELECT COUNT(*) FROM WicPipeline          WHERE PrimaryAgent       = @n"  @{n=$MohamedName}
$checks += Verify-Zero "    WicPipeline Backup"         "SELECT COUNT(*) FROM WicPipeline          WHERE BackupAgent        = @n"  @{n=$MohamedName}
$checks += Verify-Zero "    TrainingSchedule AgentIds"  "SELECT COUNT(*) FROM TrainingSchedule     WHERE AgentIds LIKE @pat"       @{pat="%$MohamedId%"}
$checks += Verify-Zero "    TrainingSchedule SuggestBy" "SELECT COUNT(*) FROM TrainingSchedule     WHERE SuggestedBy = @n OR ConfirmedBy = @n" @{n=$MohamedName}

$Conn2.Close()

$remaining = ($checks | Measure-Object -Sum).Sum
Write-Host ""
if ($remaining -eq 0) {
    Write-Host "Verification PASSED: 0 rows remain across all 34 checks." -ForegroundColor Green
} else {
    Write-Host ("Verification FAILED: {0} row(s) still present -- investigate before rebuilding." -f $remaining) -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Rebuild + restart:"
Write-Host "       pwsh -File C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1"
Write-Host "  2. Open the Find Substitute panel for any location and any date."
Write-Host "     Confirm 'Dennis Obazee' does NOT appear in the candidate list."
Write-Host "     Confirm 'Mohamed Khaled Mahmoud' does NOT appear in the candidate list."
Write-Host "  3. Check WIC Open/Overview for Neu-Isenburg (DE_NeuIsenburg)."
Write-Host "     Mohamed was the MAIN agent -- verify coverage is now shown as"
Write-Host "     unassigned/uncovered and does not cause a crash or null-ref."
Write-Host ""
