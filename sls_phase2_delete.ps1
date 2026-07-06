# sls_phase2_delete.ps1
# PHASE 2: Permanently delete Ferenc Koreh (4390267), Tunde Szabo (4390487),
#          Zsolt Fulop (4390187). All moved to SLS team.
#
# Single transaction with XACT_ABORT ON.
# ShiftEntries deleted first (by EmployeeId). All other 15 child tables run
# with expected=0 guards -- any row appearing since Phase 1 triggers ROLLBACK.
# Employees rows last. Any count mismatch => automatic ROLLBACK, nothing committed.
# After COMMIT: 51-check verification (17 per employee) asserts all counts = 0.
#
# Run with: pwsh -File C:\GSDDashboard\sls_phase2_delete.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Add-Type -AssemblyName "System.Data"

$FerencId   = "4390267"
$FerencName = "Ferenc Koreh"

$TundeId    = "4390487"
$TundeName  = "Tunde Szabo"

$ZsoltId    = "4390187"
$ZsoltName  = "Zsolt Fulop"

# Expected row counts locked from Phase 1.
#   Each employee: ShiftEntries=145, Employees=1, all others=0. Total=146 each.
#   Grand total: 438 rows.

Write-Host ""
Write-Host "=== SLS PHASE 2: PERMANENT DELETE - 3 EMPLOYEES ===" -ForegroundColor Red
Write-Host "  Ferenc Koreh  4390267  -  146 rows" -ForegroundColor Yellow
Write-Host "  Tunde Szabo   4390487  -  146 rows" -ForegroundColor Yellow
Write-Host "  Zsolt Fulop   4390187  -  146 rows" -ForegroundColor Yellow
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
# Throws on mismatch -- propagates to catch which rolls back.
# PS 5.1: assign conditional to variable; no bare 'return if'.
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
    Write-Host ("  {0,-56} deleted={1,5}  expect={2,5}" -f $Label, $n, $Expect) -ForegroundColor $color
    if (-not $ok) {
        throw ("MISMATCH [{0}]: deleted {1}, expected {2}" -f $Label, $n, $Expect)
    }
}

$CommitOk = $false

try {

    # ----------------------------------------------------------------------
    # Ferenc Koreh (4390267) - children first
    # ----------------------------------------------------------------------
    Write-Host "Ferenc Koreh (4390267):" -ForegroundColor Cyan

    Delete-Check "Ferenc / ShiftEntries"               "DELETE FROM ShiftEntries        WHERE EmployeeId         = @id" @{id=$FerencId}    145
    Delete-Check "Ferenc / WicShiftEntries"            "DELETE FROM WicShiftEntries     WHERE EmployeeId         = @id" @{id=$FerencId}      0
    Delete-Check "Ferenc / SickLeaves"                 "DELETE FROM SickLeaves          WHERE EmployeeId         = @id" @{id=$FerencId}      0
    Delete-Check "Ferenc / Vacations"                  "DELETE FROM Vacations           WHERE EmployeeId         = @id" @{id=$FerencId}      0
    Delete-Check "Ferenc / ALBalance"                  "DELETE FROM ALBalance           WHERE EmployeeId         = @id" @{id=$FerencId}      0
    Delete-Check "Ferenc / SubstitutionHistory"        "DELETE FROM SubstitutionHistory WHERE EmployeeId         = @id" @{id=$FerencId}      0
    Delete-Check "Ferenc / BreakSlots"                 "DELETE FROM BreakSlots          WHERE EmployeeId         = @id" @{id=$FerencId}      0
    Delete-Check "Ferenc / VwicRotationSlots"          "DELETE FROM VwicRotationSlots   WHERE EmployeeId         = @id" @{id=$FerencId}      0
    Delete-Check "Ferenc / DailyAttendance"            "DELETE FROM DailyAttendance     WHERE AssignedEmployeeId = @id" @{id=$FerencId}      0
    Delete-Check "Ferenc / WicAgentAssignments(name)"  "DELETE FROM WicAgentAssignments WHERE EmployeeName       = @n"  @{n=$FerencName}     0
    Delete-Check "Ferenc / AgentReachableCities(id)"   "DELETE FROM AgentReachableCities WHERE EmployeeId       = @id" @{id=$FerencId}      0
    Delete-Check "Ferenc / AgentReachableCities(name)" "DELETE FROM AgentReachableCities WHERE EmployeeName     = @n"  @{n=$FerencName}     0
    Delete-Check "Ferenc / WicPipeline Primary"        "DELETE FROM WicPipeline         WHERE PrimaryAgent       = @n"  @{n=$FerencName}     0
    Delete-Check "Ferenc / WicPipeline Backup"         "DELETE FROM WicPipeline         WHERE BackupAgent        = @n"  @{n=$FerencName}     0
    Delete-Check "Ferenc / TrainingSchedule AgentIds"  "DELETE FROM TrainingSchedule    WHERE AgentIds LIKE @pat"       @{pat="%$FerencId%"} 0
    Delete-Check "Ferenc / TrainingSchedule SuggestBy" "DELETE FROM TrainingSchedule    WHERE SuggestedBy = @n OR ConfirmedBy = @n" @{n=$FerencName} 0

    Write-Host ""

    # ----------------------------------------------------------------------
    # Tunde Szabo (4390487) - children first
    # ----------------------------------------------------------------------
    Write-Host "Tunde Szabo (4390487):" -ForegroundColor Cyan

    Delete-Check "Tunde / ShiftEntries"               "DELETE FROM ShiftEntries        WHERE EmployeeId         = @id" @{id=$TundeId}    145
    Delete-Check "Tunde / WicShiftEntries"            "DELETE FROM WicShiftEntries     WHERE EmployeeId         = @id" @{id=$TundeId}      0
    Delete-Check "Tunde / SickLeaves"                 "DELETE FROM SickLeaves          WHERE EmployeeId         = @id" @{id=$TundeId}      0
    Delete-Check "Tunde / Vacations"                  "DELETE FROM Vacations           WHERE EmployeeId         = @id" @{id=$TundeId}      0
    Delete-Check "Tunde / ALBalance"                  "DELETE FROM ALBalance           WHERE EmployeeId         = @id" @{id=$TundeId}      0
    Delete-Check "Tunde / SubstitutionHistory"        "DELETE FROM SubstitutionHistory WHERE EmployeeId         = @id" @{id=$TundeId}      0
    Delete-Check "Tunde / BreakSlots"                 "DELETE FROM BreakSlots          WHERE EmployeeId         = @id" @{id=$TundeId}      0
    Delete-Check "Tunde / VwicRotationSlots"          "DELETE FROM VwicRotationSlots   WHERE EmployeeId         = @id" @{id=$TundeId}      0
    Delete-Check "Tunde / DailyAttendance"            "DELETE FROM DailyAttendance     WHERE AssignedEmployeeId = @id" @{id=$TundeId}      0
    Delete-Check "Tunde / WicAgentAssignments(name)"  "DELETE FROM WicAgentAssignments WHERE EmployeeName       = @n"  @{n=$TundeName}     0
    Delete-Check "Tunde / AgentReachableCities(id)"   "DELETE FROM AgentReachableCities WHERE EmployeeId       = @id" @{id=$TundeId}      0
    Delete-Check "Tunde / AgentReachableCities(name)" "DELETE FROM AgentReachableCities WHERE EmployeeName     = @n"  @{n=$TundeName}     0
    Delete-Check "Tunde / WicPipeline Primary"        "DELETE FROM WicPipeline         WHERE PrimaryAgent       = @n"  @{n=$TundeName}     0
    Delete-Check "Tunde / WicPipeline Backup"         "DELETE FROM WicPipeline         WHERE BackupAgent        = @n"  @{n=$TundeName}     0
    Delete-Check "Tunde / TrainingSchedule AgentIds"  "DELETE FROM TrainingSchedule    WHERE AgentIds LIKE @pat"       @{pat="%$TundeId%"} 0
    Delete-Check "Tunde / TrainingSchedule SuggestBy" "DELETE FROM TrainingSchedule    WHERE SuggestedBy = @n OR ConfirmedBy = @n" @{n=$TundeName} 0

    Write-Host ""

    # ----------------------------------------------------------------------
    # Zsolt Fulop (4390187) - children first
    # ----------------------------------------------------------------------
    Write-Host "Zsolt Fulop (4390187):" -ForegroundColor Cyan

    Delete-Check "Zsolt / ShiftEntries"               "DELETE FROM ShiftEntries        WHERE EmployeeId         = @id" @{id=$ZsoltId}    145
    Delete-Check "Zsolt / WicShiftEntries"            "DELETE FROM WicShiftEntries     WHERE EmployeeId         = @id" @{id=$ZsoltId}      0
    Delete-Check "Zsolt / SickLeaves"                 "DELETE FROM SickLeaves          WHERE EmployeeId         = @id" @{id=$ZsoltId}      0
    Delete-Check "Zsolt / Vacations"                  "DELETE FROM Vacations           WHERE EmployeeId         = @id" @{id=$ZsoltId}      0
    Delete-Check "Zsolt / ALBalance"                  "DELETE FROM ALBalance           WHERE EmployeeId         = @id" @{id=$ZsoltId}      0
    Delete-Check "Zsolt / SubstitutionHistory"        "DELETE FROM SubstitutionHistory WHERE EmployeeId         = @id" @{id=$ZsoltId}      0
    Delete-Check "Zsolt / BreakSlots"                 "DELETE FROM BreakSlots          WHERE EmployeeId         = @id" @{id=$ZsoltId}      0
    Delete-Check "Zsolt / VwicRotationSlots"          "DELETE FROM VwicRotationSlots   WHERE EmployeeId         = @id" @{id=$ZsoltId}      0
    Delete-Check "Zsolt / DailyAttendance"            "DELETE FROM DailyAttendance     WHERE AssignedEmployeeId = @id" @{id=$ZsoltId}      0
    Delete-Check "Zsolt / WicAgentAssignments(name)"  "DELETE FROM WicAgentAssignments WHERE EmployeeName       = @n"  @{n=$ZsoltName}     0
    Delete-Check "Zsolt / AgentReachableCities(id)"   "DELETE FROM AgentReachableCities WHERE EmployeeId       = @id" @{id=$ZsoltId}      0
    Delete-Check "Zsolt / AgentReachableCities(name)" "DELETE FROM AgentReachableCities WHERE EmployeeName     = @n"  @{n=$ZsoltName}     0
    Delete-Check "Zsolt / WicPipeline Primary"        "DELETE FROM WicPipeline         WHERE PrimaryAgent       = @n"  @{n=$ZsoltName}     0
    Delete-Check "Zsolt / WicPipeline Backup"         "DELETE FROM WicPipeline         WHERE BackupAgent        = @n"  @{n=$ZsoltName}     0
    Delete-Check "Zsolt / TrainingSchedule AgentIds"  "DELETE FROM TrainingSchedule    WHERE AgentIds LIKE @pat"       @{pat="%$ZsoltId%"} 0
    Delete-Check "Zsolt / TrainingSchedule SuggestBy" "DELETE FROM TrainingSchedule    WHERE SuggestedBy = @n OR ConfirmedBy = @n" @{n=$ZsoltName} 0

    Write-Host ""

    # ----------------------------------------------------------------------
    # Employees rows - LAST, after all dependents removed
    # ----------------------------------------------------------------------
    Write-Host "Employees rows (all three) - LAST:" -ForegroundColor Cyan

    Delete-Check "Ferenc / Employees"  "DELETE FROM Employees WHERE EmployeeId = @id" @{id=$FerencId}  1
    Delete-Check "Tunde  / Employees"  "DELETE FROM Employees WHERE EmployeeId = @id" @{id=$TundeId}   1
    Delete-Check "Zsolt  / Employees"  "DELETE FROM Employees WHERE EmployeeId = @id" @{id=$ZsoltId}   1

    Write-Host ""

    # ----------------------------------------------------------------------
    # All counts matched - COMMIT
    # ----------------------------------------------------------------------
    $Txn.Commit()
    $CommitOk = $true

    $totalDeleted = (145*3) + (1*3)
    Write-Host ("COMMIT -- {0} rows deleted (expected 438)." -f $totalDeleted) -ForegroundColor Green

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
# Post-commit verification: fresh connection, 17 checks per employee = 51 total.
# Every count must be 0.
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Post-commit verification (51 checks) ..." -ForegroundColor Cyan
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
    Write-Host ("  {0,-60} {1}" -f $Label, $n) -ForegroundColor $color
    return $n
}

$checks = @()

Write-Host "  Ferenc Koreh (4390267):" -ForegroundColor Cyan
$checks += Verify-Zero "    Employees"                  "SELECT COUNT(*) FROM Employees            WHERE EmployeeId         = @id" @{id=$FerencId}
$checks += Verify-Zero "    ShiftEntries"               "SELECT COUNT(*) FROM ShiftEntries         WHERE EmployeeId         = @id" @{id=$FerencId}
$checks += Verify-Zero "    WicShiftEntries"            "SELECT COUNT(*) FROM WicShiftEntries      WHERE EmployeeId         = @id" @{id=$FerencId}
$checks += Verify-Zero "    SickLeaves"                 "SELECT COUNT(*) FROM SickLeaves           WHERE EmployeeId         = @id" @{id=$FerencId}
$checks += Verify-Zero "    Vacations"                  "SELECT COUNT(*) FROM Vacations            WHERE EmployeeId         = @id" @{id=$FerencId}
$checks += Verify-Zero "    ALBalance"                  "SELECT COUNT(*) FROM ALBalance            WHERE EmployeeId         = @id" @{id=$FerencId}
$checks += Verify-Zero "    SubstitutionHistory"        "SELECT COUNT(*) FROM SubstitutionHistory  WHERE EmployeeId         = @id" @{id=$FerencId}
$checks += Verify-Zero "    BreakSlots"                 "SELECT COUNT(*) FROM BreakSlots           WHERE EmployeeId         = @id" @{id=$FerencId}
$checks += Verify-Zero "    VwicRotationSlots"          "SELECT COUNT(*) FROM VwicRotationSlots    WHERE EmployeeId         = @id" @{id=$FerencId}
$checks += Verify-Zero "    WicAgentAssignments(name)"  "SELECT COUNT(*) FROM WicAgentAssignments  WHERE EmployeeName       = @n"  @{n=$FerencName}
$checks += Verify-Zero "    AgentReachableCities(id)"   "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeId        = @id" @{id=$FerencId}
$checks += Verify-Zero "    AgentReachableCities(name)" "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName      = @n"  @{n=$FerencName}
$checks += Verify-Zero "    DailyAttendance"            "SELECT COUNT(*) FROM DailyAttendance      WHERE AssignedEmployeeId = @id" @{id=$FerencId}
$checks += Verify-Zero "    WicPipeline Primary"        "SELECT COUNT(*) FROM WicPipeline          WHERE PrimaryAgent       = @n"  @{n=$FerencName}
$checks += Verify-Zero "    WicPipeline Backup"         "SELECT COUNT(*) FROM WicPipeline          WHERE BackupAgent        = @n"  @{n=$FerencName}
$checks += Verify-Zero "    TrainingSchedule AgentIds"  "SELECT COUNT(*) FROM TrainingSchedule     WHERE AgentIds LIKE @pat"       @{pat="%$FerencId%"}
$checks += Verify-Zero "    TrainingSchedule SuggestBy" "SELECT COUNT(*) FROM TrainingSchedule     WHERE SuggestedBy = @n OR ConfirmedBy = @n" @{n=$FerencName}

Write-Host ""
Write-Host "  Tunde Szabo (4390487):" -ForegroundColor Cyan
$checks += Verify-Zero "    Employees"                  "SELECT COUNT(*) FROM Employees            WHERE EmployeeId         = @id" @{id=$TundeId}
$checks += Verify-Zero "    ShiftEntries"               "SELECT COUNT(*) FROM ShiftEntries         WHERE EmployeeId         = @id" @{id=$TundeId}
$checks += Verify-Zero "    WicShiftEntries"            "SELECT COUNT(*) FROM WicShiftEntries      WHERE EmployeeId         = @id" @{id=$TundeId}
$checks += Verify-Zero "    SickLeaves"                 "SELECT COUNT(*) FROM SickLeaves           WHERE EmployeeId         = @id" @{id=$TundeId}
$checks += Verify-Zero "    Vacations"                  "SELECT COUNT(*) FROM Vacations            WHERE EmployeeId         = @id" @{id=$TundeId}
$checks += Verify-Zero "    ALBalance"                  "SELECT COUNT(*) FROM ALBalance            WHERE EmployeeId         = @id" @{id=$TundeId}
$checks += Verify-Zero "    SubstitutionHistory"        "SELECT COUNT(*) FROM SubstitutionHistory  WHERE EmployeeId         = @id" @{id=$TundeId}
$checks += Verify-Zero "    BreakSlots"                 "SELECT COUNT(*) FROM BreakSlots           WHERE EmployeeId         = @id" @{id=$TundeId}
$checks += Verify-Zero "    VwicRotationSlots"          "SELECT COUNT(*) FROM VwicRotationSlots    WHERE EmployeeId         = @id" @{id=$TundeId}
$checks += Verify-Zero "    WicAgentAssignments(name)"  "SELECT COUNT(*) FROM WicAgentAssignments  WHERE EmployeeName       = @n"  @{n=$TundeName}
$checks += Verify-Zero "    AgentReachableCities(id)"   "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeId        = @id" @{id=$TundeId}
$checks += Verify-Zero "    AgentReachableCities(name)" "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName      = @n"  @{n=$TundeName}
$checks += Verify-Zero "    DailyAttendance"            "SELECT COUNT(*) FROM DailyAttendance      WHERE AssignedEmployeeId = @id" @{id=$TundeId}
$checks += Verify-Zero "    WicPipeline Primary"        "SELECT COUNT(*) FROM WicPipeline          WHERE PrimaryAgent       = @n"  @{n=$TundeName}
$checks += Verify-Zero "    WicPipeline Backup"         "SELECT COUNT(*) FROM WicPipeline          WHERE BackupAgent        = @n"  @{n=$TundeName}
$checks += Verify-Zero "    TrainingSchedule AgentIds"  "SELECT COUNT(*) FROM TrainingSchedule     WHERE AgentIds LIKE @pat"       @{pat="%$TundeId%"}
$checks += Verify-Zero "    TrainingSchedule SuggestBy" "SELECT COUNT(*) FROM TrainingSchedule     WHERE SuggestedBy = @n OR ConfirmedBy = @n" @{n=$TundeName}

Write-Host ""
Write-Host "  Zsolt Fulop (4390187):" -ForegroundColor Cyan
$checks += Verify-Zero "    Employees"                  "SELECT COUNT(*) FROM Employees            WHERE EmployeeId         = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    ShiftEntries"               "SELECT COUNT(*) FROM ShiftEntries         WHERE EmployeeId         = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    WicShiftEntries"            "SELECT COUNT(*) FROM WicShiftEntries      WHERE EmployeeId         = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    SickLeaves"                 "SELECT COUNT(*) FROM SickLeaves           WHERE EmployeeId         = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    Vacations"                  "SELECT COUNT(*) FROM Vacations            WHERE EmployeeId         = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    ALBalance"                  "SELECT COUNT(*) FROM ALBalance            WHERE EmployeeId         = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    SubstitutionHistory"        "SELECT COUNT(*) FROM SubstitutionHistory  WHERE EmployeeId         = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    BreakSlots"                 "SELECT COUNT(*) FROM BreakSlots           WHERE EmployeeId         = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    VwicRotationSlots"          "SELECT COUNT(*) FROM VwicRotationSlots    WHERE EmployeeId         = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    WicAgentAssignments(name)"  "SELECT COUNT(*) FROM WicAgentAssignments  WHERE EmployeeName       = @n"  @{n=$ZsoltName}
$checks += Verify-Zero "    AgentReachableCities(id)"   "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeId        = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    AgentReachableCities(name)" "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName      = @n"  @{n=$ZsoltName}
$checks += Verify-Zero "    DailyAttendance"            "SELECT COUNT(*) FROM DailyAttendance      WHERE AssignedEmployeeId = @id" @{id=$ZsoltId}
$checks += Verify-Zero "    WicPipeline Primary"        "SELECT COUNT(*) FROM WicPipeline          WHERE PrimaryAgent       = @n"  @{n=$ZsoltName}
$checks += Verify-Zero "    WicPipeline Backup"         "SELECT COUNT(*) FROM WicPipeline          WHERE BackupAgent        = @n"  @{n=$ZsoltName}
$checks += Verify-Zero "    TrainingSchedule AgentIds"  "SELECT COUNT(*) FROM TrainingSchedule     WHERE AgentIds LIKE @pat"       @{pat="%$ZsoltId%"}
$checks += Verify-Zero "    TrainingSchedule SuggestBy" "SELECT COUNT(*) FROM TrainingSchedule     WHERE SuggestedBy = @n OR ConfirmedBy = @n" @{n=$ZsoltName}

$Conn2.Close()

$remaining = ($checks | Measure-Object -Sum).Sum
Write-Host ""
if ($remaining -eq 0) {
    Write-Host "Verification PASSED: 0 rows remain across all 51 checks." -ForegroundColor Green
} else {
    Write-Host ("Verification FAILED: {0} row(s) still present -- investigate before rebuilding." -f $remaining) -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Confirm, then remove the 3 names from the Excluded HashSet in:"
Write-Host "       Backend\Services\WicCoverageService.cs   lines 57-58"
Write-Host "       Backend\Services\WicCoverageImport.cs    lines 10-11"
Write-Host "  2. Rebuild + restart:"
Write-Host "       pwsh -File C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1"
Write-Host "  3. Open the Find Substitute panel for any location and any date."
Write-Host "     Confirm none of the three appears as a substitute candidate:"
Write-Host "       Ferenc Koreh, Tunde Szabo, Zsolt Fulop"
Write-Host ""
