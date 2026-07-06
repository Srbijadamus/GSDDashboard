# masalma_fix.ps1
# Fixes EmployeeId=9135517: set correct name/KID/email; rename name-keyed refs.
# Matches by EmployeeId only - does NOT assume what the current FullName is.

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"
$empId           = "9135517"
$newName         = "Mohammad Al Masalma"
$newKid          = "M101365"
$newEonEmail     = "Mohammad.Al.Masalma.external@eon.com"

Add-Type -AssemblyName "System.Data"
$cfg = Get-Content $appSettingsPath | ConvertFrom-Json
$cs  = $cfg.ConnectionStrings.DefaultConnection

function Open-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection($script:cs)
    $c.Open()
    return $c
}

function Scalar-Tx([string]$sql, [hashtable]$params,
                   [System.Data.SqlClient.SqlConnection]$conn,
                   [System.Data.SqlClient.SqlTransaction]$tx) {
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx
    $cmd.CommandText = $sql
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
    $cmd.CommandText = $sql
    foreach ($kv in $params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    return $cmd.ExecuteNonQuery()
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
        $rdr = $cmd.ExecuteReader()
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

Write-Host ""
Write-Host "=== masalma_fix ===" -ForegroundColor Yellow
Write-Host ""

# ==== Step 1: read current state ==============================================
Write-Host "--- Step 1: current DB state for EmployeeId=$empId ---" -ForegroundColor Cyan

$before = Invoke-Rows `
    "SELECT EmployeeId, FullName, PrimaryKid, EonEmail, InfosysEmail, IsActive FROM Employees WHERE EmployeeId = @id" `
    @{ id = $empId }

if ($before.Count -eq 0) {
    Write-Host "  ABORT: EmployeeId=$empId not found in Employees." -ForegroundColor Red
    throw "Employee $empId not found."
}

$currentName = $before[0].FullName
Write-Host ("  EmployeeId  : {0}" -f $before[0].EmployeeId)
Write-Host ("  FullName    : {0}" -f $currentName)
Write-Host ("  PrimaryKid  : {0}" -f $before[0].PrimaryKid)
Write-Host ("  EonEmail    : {0}" -f $before[0].EonEmail)
Write-Host ("  InfosysEmail: {0}" -f $before[0].InfosysEmail)
Write-Host ("  IsActive    : {0}" -f $before[0].IsActive)
Write-Host ""

# ==== Step 2: transaction =====================================================
Write-Host "--- Step 2: transaction ---" -ForegroundColor Cyan

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$tx = $null

try {
    $xaCmd = $conn.CreateCommand()
    $xaCmd.CommandText = "SET XACT_ABORT ON"
    [void]$xaCmd.ExecuteNonQuery()

    $tx = $conn.BeginTransaction()

    # 1. Update Employees by EmployeeId
    $rEmp = [int](Exec-Tx `
        "UPDATE Employees SET FullName=@newName, PrimaryKid=@kid, EonEmail=@email WHERE EmployeeId=@eid" `
        @{ newName = $newName; kid = $newKid; email = $newEonEmail; eid = $empId } `
        $conn $tx)

    Write-Host ("  Employees UPDATE       : {0} row(s)" -f $rEmp)
    if ($rEmp -ne 1) {
        throw "Employees UPDATE affected $rEmp rows (expected 1)."
    }

    # 2. Rename name-keyed refs from old name (no fixed count guard - just report)
    $rAssign = [int](Exec-Tx `
        "UPDATE WicAgentAssignments SET EmployeeName=@newName WHERE EmployeeName=@old" `
        @{ newName = $newName; old = $currentName } `
        $conn $tx)
    Write-Host ("  WicAgentAssignments    : {0} row(s) renamed" -f $rAssign)

    $rCities = [int](Exec-Tx `
        "UPDATE AgentReachableCities SET EmployeeName=@newName WHERE EmployeeName=@old" `
        @{ newName = $newName; old = $currentName } `
        $conn $tx)
    Write-Host ("  AgentReachableCities   : {0} row(s) renamed" -f $rCities)

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

# ==== Step 3: post-commit verify ==============================================
Write-Host "--- Step 3: verify ---" -ForegroundColor Cyan

$after = Invoke-Rows `
    "SELECT EmployeeId, FullName, PrimaryKid, EonEmail, InfosysEmail, IsActive FROM Employees WHERE EmployeeId = @id" `
    @{ id = $empId }

$errCount = 0

if ($after.Count -ne 1) {
    Write-Host "  FAIL: employee row not found after commit." -ForegroundColor Red
    $errCount++
} else {
    $e = $after[0]
    Write-Host ("  FullName    : {0}" -f $e.FullName)
    Write-Host ("  PrimaryKid  : {0}" -f $e.PrimaryKid)
    Write-Host ("  EonEmail    : {0}" -f $e.EonEmail)
    Write-Host ("  InfosysEmail: {0}  (unchanged)" -f $e.InfosysEmail)
    Write-Host ("  IsActive    : {0}  (unchanged)" -f $e.IsActive)

    if ($e.FullName  -ne $newName)     { Write-Host "  FAIL: FullName mismatch"    -ForegroundColor Red; $errCount++ }
    else                               { Write-Host "  FullName: OK"                -ForegroundColor Green }
    if ($e.PrimaryKid -ne $newKid)     { Write-Host "  FAIL: PrimaryKid mismatch"  -ForegroundColor Red; $errCount++ }
    else                               { Write-Host "  PrimaryKid: OK"              -ForegroundColor Green }
    if ($e.EonEmail   -ne $newEonEmail){ Write-Host "  FAIL: EonEmail mismatch"    -ForegroundColor Red; $errCount++ }
    else                               { Write-Host "  EonEmail: OK"                -ForegroundColor Green }
}

# Zero old-name refs anywhere
$staleAssign = [int](Invoke-Rows "SELECT COUNT(*) AS C FROM WicAgentAssignments WHERE EmployeeName=@n" @{ n = $currentName })[0].C
$staleCities = [int](Invoke-Rows "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName=@n" @{ n = $currentName })[0].C
$staleEmpName = [int](Invoke-Rows "SELECT COUNT(*) AS C FROM Employees WHERE FullName=@n" @{ n = $currentName })[0].C

Write-Host ""
if ($staleEmpName    -gt 0) { Write-Host ("  FAIL: Employees still has old name ({0} rows)"            -f $staleEmpName)    -ForegroundColor Red; $errCount++ }
else                        { Write-Host "  Employees old name: 0 rows  OK"                                                 -ForegroundColor Green }
if ($staleAssign     -gt 0) { Write-Host ("  FAIL: WicAgentAssignments still has old name ({0} rows)"  -f $staleAssign)     -ForegroundColor Red; $errCount++ }
else                        { Write-Host "  WicAgentAssignments old name: 0 rows  OK"                                       -ForegroundColor Green }
if ($staleCities     -gt 0) { Write-Host ("  FAIL: AgentReachableCities still has old name ({0} rows)" -f $staleCities)     -ForegroundColor Red; $errCount++ }
else                        { Write-Host "  AgentReachableCities old name: 0 rows  OK"                                      -ForegroundColor Green }

# Show current WicAgentAssignments under new name
Write-Host ""
$assigns = Invoke-Rows (
    "SELECT waa.LocationCode, waa.AssignmentType, CAST(waa.IsActive AS int) AS IsActive, " +
    "ISNULL(wl.DisplayName, waa.LocationCode) AS DisplayName " +
    "FROM WicAgentAssignments waa " +
    "LEFT JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode " +
    "WHERE waa.EmployeeName = @n ORDER BY waa.LocationCode") @{ n = $newName }

Write-Host ("  WicAgentAssignments under '{0}': {1} row(s)" -f $newName, $assigns.Count)
foreach ($a in $assigns) {
    $tag = if ($a.IsActive -eq 1) { "ACTIVE" } else { "inactive" }
    Write-Host ("    {0,-40} {1,-10} {2}" -f $a.DisplayName, $a.AssignmentType, $tag)
}

Write-Host ""
if ($errCount -eq 0) {
    Write-Host "All checks PASSED." -ForegroundColor Green
} else {
    Write-Host ("$errCount check(s) FAILED." ) -ForegroundColor Red
}

Write-Host ""
Write-Host "=== masalma_fix complete ===" -ForegroundColor Cyan
Write-Host ""
