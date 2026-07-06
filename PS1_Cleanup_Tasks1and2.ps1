# PS1_Cleanup_Tasks1and2.ps1
# TASK 1: Delete terminated-employee rows from assignment tables.
# TASK 2: Delete orphan ShiftEntries (EmployeeId not in Employees).
# Ends with a verification summary of affected integrity counts.

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"

Add-Type -AssemblyName "System.Data"
$cfg = Get-Content $appSettingsPath | ConvertFrom-Json
$cs  = $cfg.ConnectionStrings.DefaultConnection

# ── helpers ──────────────────────────────────────────────────────────────────

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

# Build a parameterized IN clause from a string array.
# Returns @{ Clause="@n0,@n1,..."; Params=@{n0=...;n1=...} }
function Build-In([string[]]$names, [string]$prefix = "n") {
    $clause = (0..($names.Count - 1) | ForEach-Object { "@${prefix}$_" }) -join ", "
    $p = @{}
    for ($i = 0; $i -lt $names.Count; $i++) { $p["${prefix}$i"] = $names[$i] }
    return @{ Clause = $clause; Params = $p }
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

# ═══════════════════════════════════════════════════════════════════════════════
# TASK 1 — Delete terminated-employee assignment rows
# ═══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "============================================================" -ForegroundColor Magenta
Write-Host " TASK 1: Delete terminated-employee assignment rows" -ForegroundColor Magenta
Write-Host "============================================================" -ForegroundColor Magenta

$terminated = @(
    'Maik Kopperschmidt'
    'Thugipan Sivanesan'
    'Uwe Sprejz'
    'Walfredo Wester'
    'Ebubekir Yildiz'
    'Salih Medik'
    'Christos Kyrillidis'
    'Dennis Obazee'
    'Mohamed Khaled Mahmoud'
)

$doNotDelete = @('Karlo Coric')          # keep all rows
$listOnly    = @('Elias Erdem', 'Patrick Henschel')   # list at end, do NOT delete

# Build the shared IN clause (used for both counts and deletes)
$inT = Build-In $terminated "t"

# ── 1a. Pre-count per name per table ─────────────────────────────────────────
Write-Host ""
Write-Host "--- Pre-counts per name ---" -ForegroundColor Cyan

$perNameWAA = @{}
$perNameARC = @{}
$perNameWP  = @{}

foreach ($name in $terminated) {
    $p = @{ nm = $name }
    $perNameWAA[$name] = Scalar "SELECT COUNT(*) AS C FROM WicAgentAssignments  WHERE EmployeeName  = @nm" $p
    $perNameARC[$name] = Scalar "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName  = @nm" $p
    $perNameWP[$name]  = Scalar "SELECT COUNT(*) AS C FROM WicPipeline WHERE (PrimaryAgent = @nm OR BackupAgent = @nm) AND ISNULL(PrimaryAgent,'') <> 'Karlo Coric' AND ISNULL(BackupAgent,'') <> 'Karlo Coric'" $p
    $rowTotal = $perNameWAA[$name] + $perNameARC[$name] + $perNameWP[$name]
    Write-Host ("  {0,-30}  WAA={1}  ARC={2}  WP={3}  total={4}" -f $name, $perNameWAA[$name], $perNameARC[$name], $perNameWP[$name], $rowTotal)
}

# ── 1b. Aggregate pre-counts for guard (single query per table) ───────────────
$preWAA = Scalar "SELECT COUNT(*) AS C FROM WicAgentAssignments  WHERE EmployeeName  IN ($($inT.Clause))" $inT.Params
$preARC = Scalar "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName  IN ($($inT.Clause))" $inT.Params
$preWP  = Scalar "SELECT COUNT(*) AS C FROM WicPipeline WHERE (PrimaryAgent IN ($($inT.Clause)) OR BackupAgent IN ($($inT.Clause))) AND ISNULL(PrimaryAgent,'') <> 'Karlo Coric' AND ISNULL(BackupAgent,'') <> 'Karlo Coric'" $inT.Params

Write-Host ""
Write-Host ("  Aggregate pre-counts:  WAA={0}  ARC={1}  WP={2}  total={3}" -f $preWAA, $preARC, $preWP, ($preWAA + $preARC + $preWP)) -ForegroundColor Yellow

if (($preWAA + $preARC + $preWP) -eq 0) {
    Write-Host "  Nothing to delete for terminated list. TASK 1 DONE." -ForegroundColor Green
} else {

    # ── 1c. Delete in one transaction ─────────────────────────────────────────
    Write-Host ""
    Write-Host "--- Deleting in one transaction ---" -ForegroundColor Cyan

    $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn.Open()
    $tx = $null

    try {
        $xaCmd = $conn.CreateCommand()
        $xaCmd.CommandText = "SET XACT_ABORT ON"
        [void]$xaCmd.ExecuteNonQuery()
        $tx = $conn.BeginTransaction()

        # Re-count inside transaction
        function In-TxScalar([string]$sql, [hashtable]$p) {
            $cmd = $conn.CreateCommand(); $cmd.Transaction = $tx; $cmd.CommandText = $sql
            foreach ($kv in $p.GetEnumerator()) { [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value) }
            $v = $cmd.ExecuteScalar()
            $result = 0; if (-not ($v -is [System.DBNull] -or $null -eq $v)) { $result = [int]$v }; return $result
        }

        $chkWAA = In-TxScalar "SELECT COUNT(*) FROM WicAgentAssignments  WHERE EmployeeName  IN ($($inT.Clause))" $inT.Params
        $chkARC = In-TxScalar "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName  IN ($($inT.Clause))" $inT.Params
        $chkWP  = In-TxScalar "SELECT COUNT(*) FROM WicPipeline WHERE (PrimaryAgent IN ($($inT.Clause)) OR BackupAgent IN ($($inT.Clause))) AND ISNULL(PrimaryAgent,'') <> 'Karlo Coric' AND ISNULL(BackupAgent,'') <> 'Karlo Coric'" $inT.Params

        if ($chkWAA -ne $preWAA) { throw "Guard: WAA pre=$preWAA in-tx=$chkWAA mismatch." }
        if ($chkARC -ne $preARC) { throw "Guard: ARC pre=$preARC in-tx=$chkARC mismatch." }
        if ($chkWP  -ne $preWP)  { throw "Guard: WP  pre=$preWP  in-tx=$chkWP  mismatch." }

        $rWAA = [int](Exec-Tx "DELETE FROM WicAgentAssignments  WHERE EmployeeName  IN ($($inT.Clause))" $inT.Params $conn $tx)
        $rARC = [int](Exec-Tx "DELETE FROM AgentReachableCities WHERE EmployeeName  IN ($($inT.Clause))" $inT.Params $conn $tx)
        $rWP  = [int](Exec-Tx "DELETE FROM WicPipeline WHERE (PrimaryAgent IN ($($inT.Clause)) OR BackupAgent IN ($($inT.Clause))) AND ISNULL(PrimaryAgent,'') <> 'Karlo Coric' AND ISNULL(BackupAgent,'') <> 'Karlo Coric'" $inT.Params $conn $tx)

        if ($rWAA -ne $preWAA) { throw "Guard: WAA expected=$preWAA deleted=$rWAA mismatch. ROLLBACK." }
        if ($rARC -ne $preARC) { throw "Guard: ARC expected=$preARC deleted=$rARC mismatch. ROLLBACK." }
        if ($rWP  -ne $preWP)  { throw "Guard: WP  expected=$preWP  deleted=$rWP  mismatch. ROLLBACK." }

        $tx.Commit()
        Write-Host ("  Deleted: WAA={0}  ARC={1}  WP={2}  total={3}" -f $rWAA, $rARC, $rWP, ($rWAA+$rARC+$rWP)) -ForegroundColor Green
        Write-Host "  Transaction committed." -ForegroundColor Green

    } catch {
        if ($null -ne $tx) { try { $tx.Rollback() } catch {} }
        Write-Host "ERROR: $_" -ForegroundColor Red
        Write-Host "Transaction rolled back. No data written." -ForegroundColor Red
        throw
    } finally {
        $conn.Close()
    }
}

# ── 1d. List "do not delete" names - verify untouched ─────────────────────────
Write-Host ""
Write-Host "--- Verifying DO-NOT-TOUCH names were not affected ---" -ForegroundColor Cyan
foreach ($name in $doNotDelete) {
    $p   = @{ nm = $name }
    $waa = Scalar "SELECT COUNT(*) AS C FROM WicAgentAssignments  WHERE EmployeeName  = @nm" $p
    $arc = Scalar "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName  = @nm" $p
    $wp  = Scalar "SELECT COUNT(*) AS C FROM WicPipeline WHERE PrimaryAgent = @nm OR BackupAgent = @nm" $p
    Write-Host ("  {0,-25}  WAA={1}  ARC={2}  WP={3}" -f $name, $waa, $arc, $wp)
}

# ── 1e. List "unknown - awaiting decision" ────────────────────────────────────
Write-Host ""
Write-Host "--- Unknown names - awaiting decision (NOT deleted) ---" -ForegroundColor Yellow
foreach ($name in $listOnly) {
    $p   = @{ nm = $name }
    $waaRows = Invoke-Rows "SELECT Id, LocationCode, AssignmentType, IsActive FROM WicAgentAssignments WHERE EmployeeName = @nm ORDER BY LocationCode" $p
    $arcRows = Invoke-Rows "SELECT Id, City FROM AgentReachableCities WHERE EmployeeName = @nm ORDER BY City" $p
    $wpRows  = Invoke-Rows "SELECT Id, LocationCode, PipelineDate, PrimaryAgent, BackupAgent FROM WicPipeline WHERE PrimaryAgent = @nm OR BackupAgent = @nm ORDER BY PipelineDate" $p
    $total = $waaRows.Count + $arcRows.Count + $wpRows.Count
    Write-Host ("  '{0}': {1} row(s) total" -f $name, $total) -ForegroundColor Yellow
    foreach ($r in $waaRows) { Write-Host ("    WicAgentAssignments:   Id={0,6}  LocationCode={1,-20}  Type={2,-8}  IsActive={3}" -f $r.Id, $r.LocationCode, $r.AssignmentType, $r.IsActive) }
    foreach ($r in $arcRows) { Write-Host ("    AgentReachableCities:  Id={0,6}  City={1}" -f $r.Id, $r.City) }
    foreach ($r in $wpRows)  { Write-Host ("    WicPipeline:           Id={0,6}  LocationCode={1,-20}  Date={2}  PA='{3}'  BA='{4}'" -f $r.Id, $r.LocationCode, $r.PipelineDate, $r.PrimaryAgent, $r.BackupAgent) }
}

Write-Host ""
Write-Host "TASK 1 DONE" -ForegroundColor Green

# ═══════════════════════════════════════════════════════════════════════════════
# TASK 2 — Delete orphan ShiftEntries
# ═══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "============================================================" -ForegroundColor Magenta
Write-Host " TASK 2: Delete orphan ShiftEntries" -ForegroundColor Magenta
Write-Host "============================================================" -ForegroundColor Magenta

$orphanSql = @"
SELECT s.EmployeeId, COUNT(*) AS RowCount, MIN(s.ShiftDate) AS FirstDate, MAX(s.ShiftDate) AS LastDate
FROM ShiftEntries s
WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)
GROUP BY s.EmployeeId
ORDER BY COUNT(*) DESC
"@

$orphanDetail = Invoke-Rows $orphanSql
$preOrphans   = ($orphanDetail | Measure-Object -Property RowCount -Sum).Sum
if ($null -eq $preOrphans) { $preOrphans = 0 }
$preOrphans   = [int]$preOrphans

Write-Host ""
Write-Host ("Total orphan ShiftEntries: {0}  across {1} distinct EmployeeId(s)" -f $preOrphans, $orphanDetail.Count) -ForegroundColor Yellow
Write-Host ""
foreach ($d in $orphanDetail) {
    Write-Host ("  EmployeeId={0,-20}  Rows={1,-6}  {2}  to  {3}" -f $d.EmployeeId, $d.RowCount, $d.FirstDate, $d.LastDate)
}

if ($preOrphans -eq 0) {
    Write-Host ""
    Write-Host "No orphan ShiftEntries found. TASK 2 DONE." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "--- Deleting in one count-guarded transaction ---" -ForegroundColor Cyan

    $conn2 = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn2.Open()
    $tx2 = $null

    try {
        $xaCmd2 = $conn2.CreateCommand()
        $xaCmd2.CommandText = "SET XACT_ABORT ON"
        [void]$xaCmd2.ExecuteNonQuery()
        $tx2 = $conn2.BeginTransaction()

        # Re-count inside transaction
        $chkCmd = $conn2.CreateCommand(); $chkCmd.Transaction = $tx2
        $chkCmd.CommandText = "SELECT COUNT(*) FROM ShiftEntries s WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)"
        $chkOrphans = [int]$chkCmd.ExecuteScalar()
        if ($chkOrphans -ne $preOrphans) { throw "Guard: pre=$preOrphans in-tx=$chkOrphans mismatch." }

        $delCmd = $conn2.CreateCommand(); $delCmd.Transaction = $tx2
        $delCmd.CommandText = "DELETE FROM ShiftEntries WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = ShiftEntries.EmployeeId)"
        $deleted2 = $delCmd.ExecuteNonQuery()

        if ($deleted2 -ne $preOrphans) { throw "Guard: expected=$preOrphans deleted=$deleted2 mismatch. ROLLBACK." }

        $tx2.Commit()
        Write-Host ("  Deleted: {0} row(s). Transaction committed." -f $deleted2) -ForegroundColor Green

    } catch {
        if ($null -ne $tx2) { try { $tx2.Rollback() } catch {} }
        Write-Host "ERROR: $_" -ForegroundColor Red
        Write-Host "Transaction rolled back." -ForegroundColor Red
        throw
    } finally {
        $conn2.Close()
    }

    # Verify
    $remaining = Scalar "SELECT COUNT(*) AS C FROM ShiftEntries s WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)"
    if ($remaining -eq 0) {
        Write-Host "  Verify: 0 orphan ShiftEntries remain. Clean." -ForegroundColor Green
    } else {
        Write-Host ("  FAIL: {0} orphan ShiftEntries still remain." -f $remaining) -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "TASK 2 DONE" -ForegroundColor Green
}

# ═══════════════════════════════════════════════════════════════════════════════
# VERIFICATION SUMMARY — new integrity counts
# ═══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "============================================================" -ForegroundColor Magenta
Write-Host " VERIFICATION SUMMARY (post-cleanup)" -ForegroundColor Magenta
Write-Host "============================================================" -ForegroundColor Magenta

# Orphan ShiftEntries
$newOrphanSE = Scalar "SELECT COUNT(*) AS C FROM ShiftEntries s WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)"
Write-Host ""
Write-Host ("Orphan ShiftEntries:                  {0}" -f $newOrphanSE) -ForegroundColor $(if ($newOrphanSE -eq 0) { "Green" } else { "Red" })

# WicAgentAssignments unresolved names
$newWAA = Scalar "SELECT COUNT(*) AS C FROM WicAgentAssignments WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = WicAgentAssignments.EmployeeName)"
Write-Host ("WicAgentAssignments unresolved names: {0}" -f $newWAA) -ForegroundColor $(if ($newWAA -eq 0) { "Green" } else { "Yellow" })
if ($newWAA -gt 0) {
    $waaNames = Invoke-Rows "SELECT DISTINCT EmployeeName FROM WicAgentAssignments WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = WicAgentAssignments.EmployeeName) ORDER BY EmployeeName"
    foreach ($r in $waaNames) { Write-Host ("  '{0}'" -f $r.EmployeeName) -ForegroundColor White }
}

# AgentReachableCities unresolved names
$newARC = Scalar "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName IS NOT NULL AND EmployeeName <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = AgentReachableCities.EmployeeName)"
Write-Host ("AgentReachableCities unresolved names:{0}" -f $newARC) -ForegroundColor $(if ($newARC -eq 0) { "Green" } else { "Yellow" })
if ($newARC -gt 0) {
    $arcNames = Invoke-Rows "SELECT DISTINCT EmployeeName FROM AgentReachableCities WHERE EmployeeName IS NOT NULL AND EmployeeName <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = AgentReachableCities.EmployeeName) ORDER BY EmployeeName"
    foreach ($r in $arcNames) { Write-Host ("  '{0}'" -f $r.EmployeeName) -ForegroundColor White }
}

# WicPipeline unresolved agent names
$newWP = Scalar @"
SELECT COUNT(*) AS C FROM WicPipeline wp
WHERE (wp.PrimaryAgent IS NOT NULL AND wp.PrimaryAgent <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.PrimaryAgent))
   OR (wp.BackupAgent  IS NOT NULL AND wp.BackupAgent  <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.BackupAgent))
"@
Write-Host ("WicPipeline unresolved names:          {0}" -f $newWP) -ForegroundColor $(if ($newWP -eq 0) { "Green" } else { "Yellow" })
if ($newWP -gt 0) {
    $wpNames = Invoke-Rows @"
SELECT wp.Id, wp.LocationCode, wp.PipelineDate, wp.PrimaryAgent, wp.BackupAgent
FROM WicPipeline wp
WHERE (wp.PrimaryAgent IS NOT NULL AND wp.PrimaryAgent <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.PrimaryAgent))
   OR (wp.BackupAgent  IS NOT NULL AND wp.BackupAgent  <> '' AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = wp.BackupAgent))
ORDER BY wp.PipelineDate DESC
"@
    foreach ($r in $wpNames) { Write-Host ("  Id={0,6}  Loc={1,-20}  Date={2}  PA='{3}'  BA='{4}'" -f $r.Id, $r.LocationCode, $r.PipelineDate, $r.PrimaryAgent, $r.BackupAgent) -ForegroundColor White }
}

Write-Host ""
Write-Host "Expected after successful run:" -ForegroundColor Cyan
Write-Host "  Orphan ShiftEntries         = 0  (all 705 deleted)" -ForegroundColor Cyan
Write-Host "  WicAgentAssignments         = 0 or only Elias Erdem / Patrick Henschel" -ForegroundColor Cyan
Write-Host "  AgentReachableCities        = 0 or only Elias Erdem / Patrick Henschel" -ForegroundColor Cyan
Write-Host "  WicPipeline                 = 0 or only Elias Erdem / Patrick Henschel / Karlo Coric" -ForegroundColor Cyan
Write-Host ""
Write-Host "All done." -ForegroundColor Green
