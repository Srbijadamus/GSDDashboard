# masalma_phase2_update.ps1
# Rename Mohammad Al Masalama -> Mohammad Al Masalma; fill PrimaryKid + EonEmail.
# Single transaction (XACT_ABORT ON). Count guards abort on mismatch.
# Tables keyed by EmployeeId (WicShiftEntries, ShiftEntries) need no name rename;
# this is confirmed by scanning their text columns inside the transaction.

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"
$baseUrl         = "http://localhost:5000"
$oldName         = "Mohammad Al Masalama"
$newName         = "Mohammad Al Masalma"
$empId           = "9135517"
$newKid          = "M101365"
$newEonEmail     = "Mohammad.Al.Masalma.external@eon.com"
$shiftDate       = "2026-07-06"

$expEmp    = 1
$expAssign = 3
$expCities = 1

Add-Type -AssemblyName "System.Data"
$cfg = Get-Content $appSettingsPath | ConvertFrom-Json
$cs  = $cfg.ConnectionStrings.DefaultConnection

# ==== Helpers =================================================================

function Open-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection($script:cs)
    $c.Open()
    return $c
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
                $v = $rdr.GetValue($i)
                $row[$rdr.GetName($i)] = if ($v -is [System.DBNull]) { $null } else { $v }
            }
            $rows += [PSCustomObject]$row
        }
        $rdr.Close()
        return $rows
    } finally { $conn.Close() }
}

function Scalar-Tx([string]$sql, [hashtable]$params,
                   [System.Data.SqlClient.SqlConnection]$conn,
                   [System.Data.SqlClient.SqlTransaction]$tx) {
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx
    $cmd.CommandText  = $sql
    foreach ($kv in $params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    $v = $cmd.ExecuteScalar()
    if ($v -is [System.DBNull]) { return $null }
    return $v
}

function Exec-Tx([string]$sql, [hashtable]$params,
                 [System.Data.SqlClient.SqlConnection]$conn,
                 [System.Data.SqlClient.SqlTransaction]$tx) {
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx
    $cmd.CommandText  = $sql
    foreach ($kv in $params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    return $cmd.ExecuteNonQuery()
}

# ==== Main ====================================================================

Write-Host ""
Write-Host "=== masalma_phase2_update ===" -ForegroundColor Yellow
Write-Host ("  Old : {0}" -f $oldName)
Write-Host ("  New : {0}   KID={1}" -f $newName, $newKid)
Write-Host ("  EID : {0}" -f $empId)
Write-Host ""

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$tx = $null

try {
    $xaCmd = $conn.CreateCommand()
    $xaCmd.CommandText = "SET XACT_ABORT ON"
    [void]$xaCmd.ExecuteNonQuery()

    $tx = $conn.BeginTransaction()

    # ---- Count guards --------------------------------------------------------
    Write-Host "--- Count guards ---" -ForegroundColor Cyan

    $cEmp    = [int](Scalar-Tx "SELECT COUNT(*) FROM Employees WHERE FullName = @n" @{ n = $oldName } $conn $tx)
    $cAssign = [int](Scalar-Tx "SELECT COUNT(*) FROM WicAgentAssignments WHERE EmployeeName = @n" @{ n = $oldName } $conn $tx)
    $cCities = [int](Scalar-Tx "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName = @n" @{ n = $oldName } $conn $tx)

    Write-Host ("  Employees            : {0}  (expected {1})" -f $cEmp,    $expEmp)
    Write-Host ("  WicAgentAssignments  : {0}  (expected {1})" -f $cAssign, $expAssign)
    Write-Host ("  AgentReachableCities : {0}  (expected {1})" -f $cCities, $expCities)

    $guardFail = $false
    if ($cEmp    -ne $expEmp)    { Write-Host "  GUARD FAIL: Employees count mismatch"            -ForegroundColor Red; $guardFail = $true }
    if ($cAssign -ne $expAssign) { Write-Host "  GUARD FAIL: WicAgentAssignments count mismatch"  -ForegroundColor Red; $guardFail = $true }
    if ($cCities -ne $expCities) { Write-Host "  GUARD FAIL: AgentReachableCities count mismatch" -ForegroundColor Red; $guardFail = $true }
    if ($guardFail) { throw "Count guard failed. Expected Emp=$expEmp Assign=$expAssign Cities=$expCities. Got $cEmp / $cAssign / $cCities." }

    Write-Host "  All count guards: OK" -ForegroundColor Green
    Write-Host ""

    # ---- WicShiftEntries / ShiftEntries: confirm ID-keyed, scan text fields --
    Write-Host "--- WicShiftEntries / ShiftEntries: key type + old-name scan ---" -ForegroundColor Cyan

    # WicShiftEntries has no EmployeeName column. Scan SupportLocation and Task.
    $wseLocScan  = [int](Scalar-Tx "SELECT COUNT(*) FROM WicShiftEntries WHERE SupportLocation = @n" @{ n = $oldName } $conn $tx)
    $wseTaskScan = [int](Scalar-Tx "SELECT COUNT(*) FROM WicShiftEntries WHERE Task = @n"            @{ n = $oldName } $conn $tx)

    # ShiftEntries has no EmployeeName column. Scan AgentTask and RawValue.
    $seTaskScan  = [int](Scalar-Tx "SELECT COUNT(*) FROM ShiftEntries WHERE AgentTask = @n"  @{ n = $oldName } $conn $tx)
    $seRawScan   = [int](Scalar-Tx "SELECT COUNT(*) FROM ShiftEntries WHERE RawValue  = @n"  @{ n = $oldName } $conn $tx)

    Write-Host "  WicShiftEntries: keyed by EmployeeId (no EmployeeName column)."
    Write-Host ("    SupportLocation = old name: {0}  Task = old name: {1}" -f $wseLocScan, $wseTaskScan)
    Write-Host "  ShiftEntries: keyed by EmployeeId (no EmployeeName column)."
    Write-Host ("    AgentTask = old name: {0}  RawValue = old name: {1}" -f $seTaskScan, $seRawScan)

    $wseNeedsRename = ($wseLocScan + $wseTaskScan) -gt 0
    $seNeedsRename  = ($seTaskScan + $seRawScan) -gt 0

    if ($wseNeedsRename -or $seNeedsRename) {
        Write-Host "  Unexpected name refs found - will rename inside transaction." -ForegroundColor Yellow
    } else {
        Write-Host "  No name refs in either table. ID-keyed rows unaffected by rename. OK." -ForegroundColor Green
    }
    Write-Host ""

    # ---- Updates -------------------------------------------------------------
    Write-Host "--- Applying updates ---" -ForegroundColor Cyan

    # 1. Employees
    $r1 = [int](Exec-Tx (
        "UPDATE Employees " +
        "SET FullName = @newName, PrimaryKid = @kid, EonEmail = @email " +
        "WHERE EmployeeId = @eid") `
        @{ newName = $newName; kid = $newKid; email = $newEonEmail; eid = $empId } $conn $tx)
    Write-Host ("  Employees            : {0} row updated" -f $r1) -ForegroundColor $(if ($r1 -eq 1) { "Green" } else { "Red" })
    if ($r1 -ne 1) { throw "Employees UPDATE affected $r1 rows (expected 1)." }

    # 2. WicAgentAssignments
    $r2 = [int](Exec-Tx (
        "UPDATE WicAgentAssignments SET EmployeeName = @newName WHERE EmployeeName = @oldName") `
        @{ newName = $newName; oldName = $oldName } $conn $tx)
    Write-Host ("  WicAgentAssignments  : {0} rows updated (expected {1})" -f $r2, $expAssign) -ForegroundColor $(if ($r2 -eq $expAssign) { "Green" } else { "Red" })
    if ($r2 -ne $expAssign) { throw "WicAgentAssignments UPDATE affected $r2 rows (expected $expAssign)." }

    # 3. AgentReachableCities
    $r3 = [int](Exec-Tx (
        "UPDATE AgentReachableCities SET EmployeeName = @newName WHERE EmployeeName = @oldName") `
        @{ newName = $newName; oldName = $oldName } $conn $tx)
    Write-Host ("  AgentReachableCities : {0} row updated (expected {1})" -f $r3, $expCities) -ForegroundColor $(if ($r3 -eq $expCities) { "Green" } else { "Red" })
    if ($r3 -ne $expCities) { throw "AgentReachableCities UPDATE affected $r3 rows (expected $expCities)." }

    # 4. WicShiftEntries (only if scan found refs)
    if ($wseNeedsRename) {
        $r4a = [int](Exec-Tx "UPDATE WicShiftEntries SET SupportLocation = @newName WHERE SupportLocation = @oldName" @{ newName = $newName; oldName = $oldName } $conn $tx)
        $r4b = [int](Exec-Tx "UPDATE WicShiftEntries SET Task = @newName WHERE Task = @oldName"                       @{ newName = $newName; oldName = $oldName } $conn $tx)
        Write-Host ("  WicShiftEntries name refs: {0} updated" -f ($r4a + $r4b)) -ForegroundColor Yellow
    }

    # 5. ShiftEntries (only if scan found refs)
    if ($seNeedsRename) {
        $r5a = [int](Exec-Tx "UPDATE ShiftEntries SET AgentTask = @newName WHERE AgentTask = @oldName" @{ newName = $newName; oldName = $oldName } $conn $tx)
        $r5b = [int](Exec-Tx "UPDATE ShiftEntries SET RawValue  = @newName WHERE RawValue  = @oldName" @{ newName = $newName; oldName = $oldName } $conn $tx)
        Write-Host ("  ShiftEntries name refs: {0} updated" -f ($r5a + $r5b)) -ForegroundColor Yellow
    }

    $tx.Commit()
    Write-Host ""
    Write-Host "  Transaction committed." -ForegroundColor Green

} catch {
    if ($null -ne $tx) { try { $tx.Rollback() } catch {} }
    Write-Host ""
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host "Transaction rolled back. No data written." -ForegroundColor Red
    throw
} finally {
    $conn.Close()
}

Write-Host ""

# ==== Post-commit verify ======================================================
Write-Host "--- Post-commit verify ---" -ForegroundColor Cyan

$errCount = 0

# 1. Zero old-name refs in all name-keyed tables
$staleEmp    = [int](Invoke-Rows "SELECT COUNT(*) AS C FROM Employees           WHERE FullName      = @n" @{ n = $oldName })[0].C
$staleAssign = [int](Invoke-Rows "SELECT COUNT(*) AS C FROM WicAgentAssignments WHERE EmployeeName  = @n" @{ n = $oldName })[0].C
$staleCities = [int](Invoke-Rows "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName = @n" @{ n = $oldName })[0].C

if ($staleEmp    -gt 0) { Write-Host ("  FAIL: Employees still has old name ({0} rows)"            -f $staleEmp)    -ForegroundColor Red; $errCount++ }
else                    { Write-Host "  Employees old name: 0 rows  OK"                                             -ForegroundColor Green }
if ($staleAssign -gt 0) { Write-Host ("  FAIL: WicAgentAssignments still has old name ({0} rows)"  -f $staleAssign) -ForegroundColor Red; $errCount++ }
else                    { Write-Host "  WicAgentAssignments old name: 0 rows  OK"                                   -ForegroundColor Green }
if ($staleCities -gt 0) { Write-Host ("  FAIL: AgentReachableCities still has old name ({0} rows)" -f $staleCities) -ForegroundColor Red; $errCount++ }
else                    { Write-Host "  AgentReachableCities old name: 0 rows  OK"                                  -ForegroundColor Green }

# 2. Employees row correct
$empRow = Invoke-Rows "SELECT EmployeeId, FullName, PrimaryKid, EonEmail, InfosysEmail, IsActive FROM Employees WHERE EmployeeId = @id" @{ id = $empId }
if ($empRow.Count -ne 1) {
    Write-Host ("  FAIL: Employees row not found for EmployeeId {0}" -f $empId) -ForegroundColor Red
    $errCount++
} else {
    $e = $empRow[0]
    Write-Host ""
    Write-Host "  Employees row:"
    Write-Host ("    EmployeeId  : {0}" -f $e.EmployeeId)
    Write-Host ("    FullName    : {0}" -f $e.FullName)
    Write-Host ("    PrimaryKid  : {0}" -f $e.PrimaryKid)
    Write-Host ("    EonEmail    : {0}" -f $e.EonEmail)
    Write-Host ("    InfosysEmail: {0}  (unchanged)" -f $e.InfosysEmail)
    Write-Host ("    IsActive    : {0}  (unchanged)" -f $e.IsActive)

    if ($e.FullName  -ne $newName)     { Write-Host "  FAIL: FullName mismatch"    -ForegroundColor Red; $errCount++ }
    if ($e.PrimaryKid -ne $newKid)     { Write-Host "  FAIL: PrimaryKid mismatch"  -ForegroundColor Red; $errCount++ }
    if ($e.EonEmail   -ne $newEonEmail){ Write-Host "  FAIL: EonEmail mismatch"    -ForegroundColor Red; $errCount++ }
    if ($e.FullName -eq $newName -and $e.PrimaryKid -eq $newKid -and $e.EonEmail -eq $newEonEmail) {
        Write-Host "  Employees fields: OK" -ForegroundColor Green
    }
}

# 3. WicAgentAssignments under new name (with DisplayName join)
Write-Host ""
$assigns = Invoke-Rows (
    "SELECT waa.LocationCode, waa.AssignmentType, CAST(waa.IsActive AS int) AS IsActive, " +
    "       ISNULL(wl.DisplayName, waa.LocationCode) AS DisplayName " +
    "FROM WicAgentAssignments waa " +
    "LEFT JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode " +
    "WHERE waa.EmployeeName = @n " +
    "ORDER BY waa.LocationCode") @{ n = $newName }

Write-Host ("  WicAgentAssignments under new name: {0} rows" -f $assigns.Count)
foreach ($a in $assigns) {
    $activeTag = if ($a.IsActive -eq 1) { "ACTIVE" } else { "inactive" }
    Write-Host ("    {0,-40} {1,-10} {2}" -f $a.DisplayName, $a.AssignmentType, $activeTag)
}

$hasNeuIsenburg = $false
$hasSiegen      = $false
foreach ($a in $assigns) {
    if ($a.DisplayName -like '*Isenburg*' -or $a.LocationCode -like '*Isenburg*') { $hasNeuIsenburg = $true }
    if ($a.DisplayName -like '*Siegen*'   -or $a.LocationCode -like '*Siegen*')   { $hasSiegen      = $true }
}
if (-not $hasNeuIsenburg) { Write-Host "  FAIL: Neu-Isenburg assignment not found under new name" -ForegroundColor Red; $errCount++ }
else                      { Write-Host "  Neu-Isenburg assignment: present  OK" -ForegroundColor Green }
if (-not $hasSiegen)      { Write-Host "  FAIL: Siegen assignment not found under new name"       -ForegroundColor Red; $errCount++ }
else                      { Write-Host "  Siegen assignment: present  OK"                         -ForegroundColor Green }

# 4. 2026-07-06 shifts (EmployeeId-keyed, not affected by rename)
Write-Host ""
$seCount  = [int](Invoke-Rows "SELECT COUNT(*) AS C FROM ShiftEntries    WHERE EmployeeId = @id AND ShiftDate = @d" @{ id = $empId; d = $shiftDate })[0].C
$wseCount = [int](Invoke-Rows "SELECT COUNT(*) AS C FROM WicShiftEntries WHERE EmployeeId = @id AND ShiftDate = @d" @{ id = $empId; d = $shiftDate })[0].C
Write-Host ("  ShiftEntry    {0} (EmployeeId={1}): {2} row" -f $shiftDate, $empId, $seCount)
Write-Host ("  WicShiftEntry {0} (EmployeeId={1}): {2} row" -f $shiftDate, $empId, $wseCount)
if ($seCount -gt 0 -and $wseCount -gt 0) {
    Write-Host "  Today's shifts: present  OK" -ForegroundColor Green
} else {
    Write-Host "  WARNING: one or both today-shifts missing (confirm PS1_68 ran for this employee)" -ForegroundColor Yellow
}

# ==== API verify ==============================================================
Write-Host ""
Write-Host "--- API verify (requires running server) ---" -ForegroundColor Cyan

try {
    $resp = Invoke-WebRequest -Uri "$baseUrl/api/wic-coverage/agents/$newKid" `
            -Method GET -TimeoutSec 10 -UseBasicParsing
    if ($resp.StatusCode -eq 200) {
        $j = $resp.Content | ConvertFrom-Json
        Write-Host ("  GET /api/wic-coverage/agents/{0}  HTTP 200" -f $newKid) -ForegroundColor Green
        Write-Host ("    fullName    : {0}" -f $j.fullName)
        Write-Host ("    primaryKid  : {0}" -f $j.primaryKid)
        Write-Host ("    eonEmail    : {0}" -f $j.eonEmail)
        $roles = @($j.wicRoles)
        Write-Host ("    wicRoles    : {0}" -f $roles.Count)
        foreach ($role in $roles) {
            Write-Host ("      {0,-35} [{1}]" -f $role.displayName, $role.assignmentType)
        }
        if ($j.primaryKid -ne $newKid) {
            Write-Host "  FAIL: API returned wrong KID" -ForegroundColor Red
            $errCount++
        }
    } else {
        Write-Host ("  HTTP {0} from /api/wic-coverage/agents/{1}" -f $resp.StatusCode, $newKid) -ForegroundColor Yellow
    }
} catch {
    Write-Host "  Server not reachable (start app first for live check)." -ForegroundColor Yellow
}

try {
    $resp2 = Invoke-WebRequest -Uri "$baseUrl/api/wic/open?date=$shiftDate&horizon=1" `
             -Method GET -TimeoutSec 10 -UseBasicParsing
    if ($resp2.StatusCode -eq 200) {
        $days = $resp2.Content | ConvertFrom-Json
        $neuLocs = @($days[0].locations | Where-Object {
            ($_.locationCode -like '*NeuIsenburg*' -or $_.locationCode -like '*Isenburg*') -and $_.scheduledCount -gt 0
        })
        if ($neuLocs.Count -gt 0) {
            Write-Host ("  Neu-Isenburg in /api/wic/open: scheduledCount={0}  effectiveCoverage={1}  OK" -f $neuLocs[0].scheduledCount, $neuLocs[0].effectiveCoverage) -ForegroundColor Green
        } else {
            Write-Host "  Neu-Isenburg not found with scheduledCount>0 in /api/wic/open (check opening hours)" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Host "  WIC open check skipped (server not reachable)." -ForegroundColor Yellow
}

# ==== Summary =================================================================
Write-Host ""
if ($errCount -eq 0) {
    Write-Host "All post-commit checks PASSED." -ForegroundColor Green
} else {
    Write-Host ("$errCount check(s) FAILED -- review output above." ) -ForegroundColor Red
}

Write-Host ""
Write-Host "=== masalma_phase2_update complete ===" -ForegroundColor Cyan
Write-Host ""
