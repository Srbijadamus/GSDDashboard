# PS1_31_SickLeaveDuplicateCheck.ps1
# Diagnostic only - no changes made

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)

function Run-Query($conn, [string]$title, [string]$sql) {
    Write-Host ""
    Write-Host "--- $title ---" -ForegroundColor Cyan
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $r = $cmd.ExecuteReader()
    $cols = @(0..($r.FieldCount - 1) | ForEach-Object { $r.GetName($_) })
    Write-Host ($cols -join "  |  ") -ForegroundColor Yellow
    Write-Host ("-" * 90) -ForegroundColor DarkGray
    $rowCount = 0
    while ($r.Read()) {
        $vals = $cols | ForEach-Object {
            if ($r.IsDBNull($r.GetOrdinal($_))) { "NULL" } else { $r[$_].ToString() }
        }
        Write-Host ($vals -join "  |  ")
        $rowCount++
    }
    $r.Close()
    Write-Host "  ($rowCount rows)" -ForegroundColor DarkGray
}

try {
    $conn.Open()
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Yellow
    Write-Host "  PS1_31: SickLeave Duplicate Check" -ForegroundColor Yellow
    Write-Host "============================================" -ForegroundColor Yellow

    Run-Query $conn "1. Duplicate groups (same EmpId+FirstDay+LastDay+LeaveType)" "
SELECT EmployeeId, FirstDay, LastDay, LeaveType, COUNT(*) AS cnt
FROM SickLeaves
GROUP BY EmployeeId, FirstDay, LastDay, LeaveType
HAVING COUNT(*) > 1
ORDER BY cnt DESC, EmployeeId
"

    Run-Query $conn "2. All rows for 9074557 (ordered by FirstDay)" "
SELECT EmployeeId, FirstDay, LastDay, LeaveType
FROM SickLeaves
WHERE EmployeeId = '9074557'
ORDER BY FirstDay
"

    Run-Query $conn "3. Total row count and distinct LeaveType values" "
SELECT LeaveType, COUNT(*) AS RowCount
FROM SickLeaves
GROUP BY LeaveType
ORDER BY LeaveType
"

    Run-Query $conn "4. Sample of ALL columns for 9074557 (to see full schema)" "
SELECT TOP 10 *
FROM SickLeaves
WHERE EmployeeId = '9074557'
ORDER BY FirstDay
"

} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
} finally {
    $conn.Close()
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "  PS1_31 complete (read-only)" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
