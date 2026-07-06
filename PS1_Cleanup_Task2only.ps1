# PS1_Cleanup_Task2only.ps1
# Delete orphan ShiftEntries (EmployeeId not in Employees).
# Task 1 was already committed - this script runs Task 2 only.
# Fix: RowCount is a reserved word in SQL Server; alias replaced with Cnt.

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

Write-Host ""
Write-Host "============================================================" -ForegroundColor Magenta
Write-Host " TASK 2: Delete orphan ShiftEntries" -ForegroundColor Magenta
Write-Host "============================================================" -ForegroundColor Magenta

# ══════════════════════════════════════════════════════════════════
# PHASE 1: Report - distinct EmployeeIds, row count, date range
# ══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Phase 1: Orphan report ---" -ForegroundColor Cyan

# Scalar pre-count (no alias collision possible with COUNT(*) AS C)
$preOrphans = Scalar "SELECT COUNT(*) AS C FROM ShiftEntries s WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)"

# Detail per EmployeeId - Cnt replaces RowCount (reserved word)
$orphanDetail = Invoke-Rows @"
SELECT s.EmployeeId,
       COUNT(*)         AS Cnt,
       MIN(s.ShiftDate) AS FirstDate,
       MAX(s.ShiftDate) AS LastDate
FROM ShiftEntries s
WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)
GROUP BY s.EmployeeId
ORDER BY COUNT(*) DESC
"@

Write-Host ("Total orphan ShiftEntries : {0}" -f $preOrphans) -ForegroundColor $(if ($preOrphans -eq 0) { "Green" } else { "Yellow" })
Write-Host ("Distinct orphan EmployeeIds: {0}" -f $orphanDetail.Count) -ForegroundColor $(if ($orphanDetail.Count -eq 0) { "Green" } else { "Yellow" })
Write-Host ""
foreach ($d in $orphanDetail) {
    Write-Host ("  EmployeeId={0,-20}  Rows={1,-6}  {2}  to  {3}" -f $d.EmployeeId, $d.Cnt, $d.FirstDate, $d.LastDate)
}

if ($preOrphans -eq 0) {
    Write-Host ""
    Write-Host "No orphan ShiftEntries found. Nothing to delete. DONE." -ForegroundColor Green
    exit 0
}

# ══════════════════════════════════════════════════════════════════
# PHASE 2: Delete in one count-guarded transaction
# ══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Phase 2: Deleting in one count-guarded transaction ---" -ForegroundColor Cyan

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$tx = $null

try {
    $xaCmd = $conn.CreateCommand()
    $xaCmd.CommandText = "SET XACT_ABORT ON"
    [void]$xaCmd.ExecuteNonQuery()
    $tx = $conn.BeginTransaction()

    # Re-count inside transaction to guard against concurrent changes
    $chkCmd = $conn.CreateCommand()
    $chkCmd.Transaction = $tx
    $chkCmd.CommandText = "SELECT COUNT(*) FROM ShiftEntries s WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)"
    $chkOrphans = [int]$chkCmd.ExecuteScalar()

    if ($chkOrphans -ne $preOrphans) {
        throw "Guard failed: pre-tx count=$preOrphans, in-tx count=$chkOrphans. Aborting."
    }

    $delCmd = $conn.CreateCommand()
    $delCmd.Transaction = $tx
    $delCmd.CommandText = "DELETE FROM ShiftEntries WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = ShiftEntries.EmployeeId)"
    $deleted = $delCmd.ExecuteNonQuery()

    if ($deleted -ne $preOrphans) {
        throw "Guard failed: expected to delete $preOrphans rows, DELETE affected $deleted rows. Rolling back."
    }

    $tx.Commit()
    Write-Host ("  Deleted: {0} row(s). Transaction committed." -f $deleted) -ForegroundColor Green

} catch {
    if ($null -ne $tx) { try { $tx.Rollback() } catch {} }
    Write-Host ""
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host "Transaction rolled back. No rows deleted." -ForegroundColor Red
    throw
} finally {
    $conn.Close()
}

# ══════════════════════════════════════════════════════════════════
# PHASE 3: Verify 0 remain
# ══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Phase 3: Verify ---" -ForegroundColor Cyan

$remaining = Scalar "SELECT COUNT(*) AS C FROM ShiftEntries s WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)"

if ($remaining -eq 0) {
    Write-Host "Orphan ShiftEntries remaining: 0. All clean." -ForegroundColor Green
} else {
    Write-Host ("FAIL: {0} orphan ShiftEntries still remain after delete." -f $remaining) -ForegroundColor Red
}

Write-Host ""
Write-Host "TASK 2 DONE" -ForegroundColor Green
