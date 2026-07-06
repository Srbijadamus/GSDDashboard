# PS1_Step3_DeleteOrphans.ps1
# Deletes orphan ShiftEntries whose EmployeeId is not in Employees.
# Phase 1: report distinct EmployeeIds, row counts, date ranges.
# Phase 2: delete in one count-guarded transaction.

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

function Scalar([string]$sql, [hashtable]$params = @{}) {
    $rows = Invoke-Rows $sql $params
    if ($rows.Count -eq 0) { return 0 }
    $first = $rows[0]
    $prop = ($first | Get-Member -MemberType NoteProperty | Select-Object -First 1).Name
    $v = $first.$prop
    if ($null -eq $v) { return 0 }
    return [int]$v
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Magenta
Write-Host " STEP 3: DELETE ORPHAN ShiftEntries" -ForegroundColor Magenta
Write-Host "=========================================" -ForegroundColor Magenta

# ══════════════════════════════════════════════════════════════════
# PHASE 1: Report
# ══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Phase 1: Orphan ShiftEntries report ---" -ForegroundColor Cyan

$totalOrphans = Scalar @"
SELECT COUNT(*) AS C FROM ShiftEntries s
WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)
"@
Write-Host ("Total orphan ShiftEntries: {0}" -f $totalOrphans) -ForegroundColor $(if ($totalOrphans -eq 0) { "Green" } else { "Yellow" })

if ($totalOrphans -eq 0) {
    Write-Host "Nothing to delete. DONE." -ForegroundColor Green
    exit 0
}

# Distinct EmployeeIds with count and date range
$detail = Invoke-Rows @"
SELECT s.EmployeeId,
       COUNT(*)          AS RowCount,
       MIN(s.ShiftDate)  AS FirstDate,
       MAX(s.ShiftDate)  AS LastDate
FROM ShiftEntries s
WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)
GROUP BY s.EmployeeId
ORDER BY COUNT(*) DESC
"@

Write-Host ("Distinct orphan EmployeeIds: {0}" -f $detail.Count)
Write-Host ""
foreach ($d in $detail) {
    Write-Host ("  EmployeeId={0,-20} Rows={1,-6} DateRange={2} to {3}" -f `
        $d.EmployeeId, $d.RowCount, $d.FirstDate, $d.LastDate)
}

Write-Host ""
Write-Host ("Total to delete: {0} rows across {1} EmployeeIds." -f $totalOrphans, $detail.Count) -ForegroundColor Yellow

# ══════════════════════════════════════════════════════════════════
# PHASE 2: Delete in guarded transaction
# ══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Phase 2: Deleting orphans (count-guarded transaction) ---" -ForegroundColor Cyan

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$tx = $null

try {
    $xaCmd = $conn.CreateCommand()
    $xaCmd.CommandText = "SET XACT_ABORT ON"
    [void]$xaCmd.ExecuteNonQuery()
    $tx = $conn.BeginTransaction()

    # Re-count inside transaction to guard against concurrent changes
    $countCmd = $conn.CreateCommand()
    $countCmd.Transaction = $tx
    $countCmd.CommandText = @"
SELECT COUNT(*) FROM ShiftEntries s
WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)
"@
    $countInTx = [int]$countCmd.ExecuteScalar()

    if ($countInTx -ne $totalOrphans) {
        throw "Guard failed: pre-transaction count was $totalOrphans but in-transaction count is $countInTx. Aborting."
    }

    # Delete
    $delCmd = $conn.CreateCommand()
    $delCmd.Transaction = $tx
    $delCmd.CommandText = @"
DELETE FROM ShiftEntries
WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = ShiftEntries.EmployeeId)
"@
    $deleted = $delCmd.ExecuteNonQuery()

    if ($deleted -ne $totalOrphans) {
        throw "Guard failed: expected to delete $totalOrphans rows but DELETE affected $deleted rows. Rolling back."
    }

    $tx.Commit()
    Write-Host ("  Deleted: {0} rows. Transaction committed." -f $deleted) -ForegroundColor Green

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
# PHASE 3: Post-delete verify
# ══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Phase 3: Verify ---" -ForegroundColor Cyan

$remaining = Scalar @"
SELECT COUNT(*) AS C FROM ShiftEntries s
WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmployeeId)
"@

if ($remaining -eq 0) {
    Write-Host "Orphan ShiftEntries remaining: 0. All clean." -ForegroundColor Green
} else {
    Write-Host ("FAIL: {0} orphan ShiftEntries still remain after delete." -f $remaining) -ForegroundColor Red
}

Write-Host ""
Write-Host "Step 3 complete." -ForegroundColor Cyan
