# PS1_Cleanup_ElisPatrick.ps1
# Delete all assignment rows for Elias Erdem and Patrick Henschel (terminated).
# Tables: WicAgentAssignments (EmployeeName), AgentReachableCities (EmployeeName),
#         WicPipeline (PrimaryAgent / BackupAgent).
# Karlo Coric rows are never touched.

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"

Add-Type -AssemblyName "System.Data"
$cfg = Get-Content $appSettingsPath | ConvertFrom-Json
$cs  = $cfg.ConnectionStrings.DefaultConnection

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
                $rowVal = $v; if ($v -is [System.DBNull]) { $rowVal = $null }
                $row[$rdr.GetName($i)] = $rowVal
            }
            $rows += [PSCustomObject]$row
        }
        $rdr.Close()
        return $rows
    } finally { $conn.Close() }
}

function Scalar([string]$sql, [hashtable]$params = @{}) {
    $r = Invoke-Rows $sql $params
    if ($r.Count -eq 0) { return 0 }
    $prop = ($r[0] | Get-Member -MemberType NoteProperty | Select-Object -First 1).Name
    $v = $r[0].$prop
    $result = [int]$v; if ($null -eq $v) { $result = 0 }; return $result
}

$n0 = 'Elias Erdem'
$n1 = 'Patrick Henschel'

# WicPipeline rows where Karlo appears in either field must never be deleted.
$karloGuard = "AND ISNULL(PrimaryAgent,'') <> 'Karlo Coric' AND ISNULL(BackupAgent,'') <> 'Karlo Coric'"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Magenta
Write-Host " DELETE: Elias Erdem + Patrick Henschel (terminated)" -ForegroundColor Magenta
Write-Host "============================================================" -ForegroundColor Magenta

# ══════════════════════════════════════════════════════════════════
# PHASE 1: Pre-counts per name per table
# ══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Phase 1: Pre-counts ---" -ForegroundColor Cyan

$waa0 = Scalar "SELECT COUNT(*) AS C FROM WicAgentAssignments  WHERE EmployeeName  = @nm" @{ nm = $n0 }
$arc0 = Scalar "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName  = @nm" @{ nm = $n0 }
$wp0  = Scalar "SELECT COUNT(*) AS C FROM WicPipeline WHERE (PrimaryAgent = @nm OR BackupAgent = @nm) $karloGuard" @{ nm = $n0 }

$waa1 = Scalar "SELECT COUNT(*) AS C FROM WicAgentAssignments  WHERE EmployeeName  = @nm" @{ nm = $n1 }
$arc1 = Scalar "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName  = @nm" @{ nm = $n1 }
$wp1  = Scalar "SELECT COUNT(*) AS C FROM WicPipeline WHERE (PrimaryAgent = @nm OR BackupAgent = @nm) $karloGuard" @{ nm = $n1 }

Write-Host ("  {0,-22}  WAA={1}  ARC={2}  WP={3}  total={4}" -f $n0, $waa0, $arc0, $wp0, ($waa0+$arc0+$wp0))
Write-Host ("  {0,-22}  WAA={1}  ARC={2}  WP={3}  total={4}" -f $n1, $waa1, $arc1, $wp1, ($waa1+$arc1+$wp1))

$preWAA = $waa0 + $waa1
$preARC = $arc0 + $arc1
$preWP  = $wp0  + $wp1

Write-Host ""
Write-Host ("  Combined:  WAA={0}  ARC={1}  WP={2}  total={3}" -f $preWAA, $preARC, $preWP, ($preWAA+$preARC+$preWP)) -ForegroundColor Yellow

if (($preWAA + $preARC + $preWP) -eq 0) {
    Write-Host ""
    Write-Host "Nothing to delete. DONE." -ForegroundColor Green
    exit 0
}

# ══════════════════════════════════════════════════════════════════
# PHASE 2: Delete in one count-guarded transaction
# ══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Phase 2: Delete (count-guarded transaction) ---" -ForegroundColor Cyan

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$tx = $null

try {
    $xaCmd = $conn.CreateCommand()
    $xaCmd.CommandText = "SET XACT_ABORT ON"
    [void]$xaCmd.ExecuteNonQuery()
    $tx = $conn.BeginTransaction()

    # In-tx re-counts (guard against concurrent row changes)
    $cWAA = $conn.CreateCommand(); $cWAA.Transaction = $tx
    $cWAA.CommandText = "SELECT COUNT(*) FROM WicAgentAssignments WHERE EmployeeName IN (@n0, @n1)"
    [void]$cWAA.Parameters.AddWithValue("@n0", $n0)
    [void]$cWAA.Parameters.AddWithValue("@n1", $n1)
    $chkWAA = [int]$cWAA.ExecuteScalar()

    $cARC = $conn.CreateCommand(); $cARC.Transaction = $tx
    $cARC.CommandText = "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName IN (@n0, @n1)"
    [void]$cARC.Parameters.AddWithValue("@n0", $n0)
    [void]$cARC.Parameters.AddWithValue("@n1", $n1)
    $chkARC = [int]$cARC.ExecuteScalar()

    $cWP = $conn.CreateCommand(); $cWP.Transaction = $tx
    $cWP.CommandText = @"
SELECT COUNT(*) FROM WicPipeline
WHERE (PrimaryAgent IN (@n0, @n1) OR BackupAgent IN (@n0, @n1))
$karloGuard
"@
    [void]$cWP.Parameters.AddWithValue("@n0", $n0)
    [void]$cWP.Parameters.AddWithValue("@n1", $n1)
    $chkWP = [int]$cWP.ExecuteScalar()

    if ($chkWAA -ne $preWAA) { throw "Guard: WAA pre=$preWAA in-tx=$chkWAA mismatch." }
    if ($chkARC -ne $preARC) { throw "Guard: ARC pre=$preARC in-tx=$chkARC mismatch." }
    if ($chkWP  -ne $preWP)  { throw "Guard: WP  pre=$preWP  in-tx=$chkWP  mismatch." }

    # Delete WAA
    $dWAA = $conn.CreateCommand(); $dWAA.Transaction = $tx
    $dWAA.CommandText = "DELETE FROM WicAgentAssignments WHERE EmployeeName IN (@n0, @n1)"
    [void]$dWAA.Parameters.AddWithValue("@n0", $n0)
    [void]$dWAA.Parameters.AddWithValue("@n1", $n1)
    $rWAA = [int]$dWAA.ExecuteNonQuery()

    # Delete ARC
    $dARC = $conn.CreateCommand(); $dARC.Transaction = $tx
    $dARC.CommandText = "DELETE FROM AgentReachableCities WHERE EmployeeName IN (@n0, @n1)"
    [void]$dARC.Parameters.AddWithValue("@n0", $n0)
    [void]$dARC.Parameters.AddWithValue("@n1", $n1)
    $rARC = [int]$dARC.ExecuteNonQuery()

    # Delete WicPipeline (Karlo guard applied)
    $dWP = $conn.CreateCommand(); $dWP.Transaction = $tx
    $dWP.CommandText = @"
DELETE FROM WicPipeline
WHERE (PrimaryAgent IN (@n0, @n1) OR BackupAgent IN (@n0, @n1))
$karloGuard
"@
    [void]$dWP.Parameters.AddWithValue("@n0", $n0)
    [void]$dWP.Parameters.AddWithValue("@n1", $n1)
    $rWP = [int]$dWP.ExecuteNonQuery()

    if ($rWAA -ne $preWAA) { throw "Guard: WAA expected=$preWAA deleted=$rWAA mismatch. ROLLBACK." }
    if ($rARC -ne $preARC) { throw "Guard: ARC expected=$preARC deleted=$rARC mismatch. ROLLBACK." }
    if ($rWP  -ne $preWP)  { throw "Guard: WP  expected=$preWP  deleted=$rWP  mismatch. ROLLBACK." }

    $tx.Commit()
    Write-Host ("  Deleted: WAA={0}  ARC={1}  WP={2}  total={3}" -f $rWAA, $rARC, $rWP, ($rWAA+$rARC+$rWP)) -ForegroundColor Green
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

# ══════════════════════════════════════════════════════════════════
# PHASE 3: Verify 0 rows remain for each name in each table
# ══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Phase 3: Verify per name ---" -ForegroundColor Cyan

$errCount = 0
foreach ($name in @($n0, $n1)) {
    $pn   = @{ nm = $name }
    $vWAA = Scalar "SELECT COUNT(*) AS C FROM WicAgentAssignments  WHERE EmployeeName  = @nm" $pn
    $vARC = Scalar "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName  = @nm" $pn
    $vWP  = Scalar "SELECT COUNT(*) AS C FROM WicPipeline WHERE (PrimaryAgent = @nm OR BackupAgent = @nm)" $pn
    $vTot = $vWAA + $vARC + $vWP
    if ($vTot -eq 0) {
        Write-Host ("  OK  : '{0}'  WAA={1}  ARC={2}  WP={3}" -f $name, $vWAA, $vARC, $vWP) -ForegroundColor Green
    } else {
        Write-Host ("  FAIL: '{0}'  WAA={1}  ARC={2}  WP={3}" -f $name, $vWAA, $vARC, $vWP) -ForegroundColor Red
        $errCount++
    }
}
if ($errCount -gt 0) {
    Write-Host ""
    Write-Host ("$errCount name(s) still have rows - investigate.") -ForegroundColor Red
}

# ══════════════════════════════════════════════════════════════════
# PHASE 4: Full integrity check - unresolved names in each table
# ══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Phase 4: Integrity check (unresolved names) ---" -ForegroundColor Cyan
Write-Host "    Expected: 0 everywhere; WicPipeline may show Karlo Coric (valid TL)." -ForegroundColor DarkGray

$unreWAA = Scalar "SELECT COUNT(*) AS C FROM WicAgentAssignments WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = WicAgentAssignments.EmployeeName)"

Write-Host ("  WicAgentAssignments unresolved  : {0}" -f $unreWAA) -ForegroundColor $(if ($unreWAA -eq 0) { "Green" } else { "Yellow" })
if ($unreWAA -gt 0) {
    $waaNames = Invoke-Rows "SELECT DISTINCT EmployeeName FROM WicAgentAssignments WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = WicAgentAssignments.EmployeeName) ORDER BY EmployeeName"
    foreach ($r in $waaNames) { Write-Host ("    '{0}'" -f $r.EmployeeName) -ForegroundColor White }
}

$unreARC = Scalar "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName IS NOT NULL AND EmployeeName <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = AgentReachableCities.EmployeeName)"

Write-Host ("  AgentReachableCities unresolved : {0}" -f $unreARC) -ForegroundColor $(if ($unreARC -eq 0) { "Green" } else { "Yellow" })
if ($unreARC -gt 0) {
    $arcNames = Invoke-Rows "SELECT DISTINCT EmployeeName FROM AgentReachableCities WHERE EmployeeName IS NOT NULL AND EmployeeName <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = AgentReachableCities.EmployeeName) ORDER BY EmployeeName"
    foreach ($r in $arcNames) { Write-Host ("    '{0}'" -f $r.EmployeeName) -ForegroundColor White }
}

$unreWP = Scalar @"
SELECT COUNT(*) AS C FROM WicPipeline wp
WHERE  (wp.PrimaryAgent IS NOT NULL AND wp.PrimaryAgent <> ''
        AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.PrimaryAgent))
    OR (wp.BackupAgent  IS NOT NULL AND wp.BackupAgent  <> ''
        AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.BackupAgent))
"@

Write-Host ("  WicPipeline unresolved          : {0}  (Karlo Coric = valid TL)" -f $unreWP) -ForegroundColor $(if ($unreWP -eq 0) { "Green" } else { "Yellow" })
if ($unreWP -gt 0) {
    $wpNames = Invoke-Rows @"
SELECT wp.Id, wp.LocationCode, wp.PipelineDate, wp.PrimaryAgent, wp.BackupAgent
FROM WicPipeline wp
WHERE  (wp.PrimaryAgent IS NOT NULL AND wp.PrimaryAgent <> ''
        AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.PrimaryAgent))
    OR (wp.BackupAgent  IS NOT NULL AND wp.BackupAgent  <> ''
        AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.BackupAgent))
ORDER BY wp.PipelineDate DESC
"@
    foreach ($r in $wpNames) {
        Write-Host ("    Id={0,6}  Loc={1,-20}  Date={2}  PA='{3}'  BA='{4}'" -f $r.Id, $r.LocationCode, $r.PipelineDate, $r.PrimaryAgent, $r.BackupAgent) -ForegroundColor White
    }
}

Write-Host ""
Write-Host "All done." -ForegroundColor Green
