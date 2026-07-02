$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=True;TrustServerCertificate=True"
$conn.Open()

# ── Step 1: Show duplicates ────────────────────────────────────────────────────
Write-Host "=== Duplicate ShiftEntries (same EmployeeId + ShiftDate) ===" -ForegroundColor Yellow

$sqlDupes = @"
SELECT e.FullName, se.EmployeeId, se.ShiftDate, COUNT(*) AS cnt,
       STRING_AGG(CAST(se.Id AS VARCHAR) + ':' + ISNULL(se.SourceSheet,'?') + '/' + ISNULL(se.ShiftType,'?'), '  |  ')
           WITHIN GROUP (ORDER BY se.Id) AS rows
FROM ShiftEntries se
LEFT JOIN Employees e ON e.EmployeeId = se.EmployeeId
GROUP BY se.EmployeeId, se.ShiftDate, e.FullName
HAVING COUNT(*) > 1
ORDER BY se.ShiftDate, se.EmployeeId
"@

$cmd = $conn.CreateCommand()
$cmd.CommandText = $sqlDupes
$rdr = $cmd.ExecuteReader()

$dupeRows = @()
while ($rdr.Read()) {
    $dupeRows += [PSCustomObject]@{
        FullName   = if ($rdr.IsDBNull(0)) { $rdr["EmployeeId"] } else { $rdr["FullName"] }
        EmployeeId = $rdr["EmployeeId"]
        ShiftDate  = $rdr["ShiftDate"].ToString("yyyy-MM-dd")
        Count      = $rdr["cnt"]
        Rows       = $rdr[4]
    }
}
$rdr.Close()

if ($dupeRows.Count -eq 0) {
    Write-Host "  No duplicates found - nothing to delete." -ForegroundColor Green
    $conn.Close()
    exit
}

Write-Host "  Found $($dupeRows.Count) EmployeeId+ShiftDate combinations with duplicates:" -ForegroundColor Red
$dupeRows | Format-Table FullName, ShiftDate, Count, Rows -AutoSize

# Total extra rows to be deleted
$totalExtra = ($dupeRows | Measure-Object -Property Count -Sum).Sum - $dupeRows.Count
Write-Host "  Total rows to delete: $totalExtra (keeping 1 per pair)" -ForegroundColor Yellow
Write-Host ""

# ── Step 2: Delete duplicates keeping highest Id ───────────────────────────────
Write-Host "Deleting older duplicates (keeping MAX(Id) per EmployeeId+ShiftDate)..." -ForegroundColor Cyan

$sqlDelete = @"
DELETE FROM ShiftEntries
WHERE Id NOT IN (
    SELECT MAX(Id)
    FROM ShiftEntries
    GROUP BY EmployeeId, ShiftDate
)
"@

$cmdDel = $conn.CreateCommand()
$cmdDel.CommandText = $sqlDelete
$deleted = $cmdDel.ExecuteNonQuery()

Write-Host "  Deleted $deleted rows." -ForegroundColor Green

# ── Step 3: Verify no duplicates remain ───────────────────────────────────────
$cmdCheck = $conn.CreateCommand()
$cmdCheck.CommandText = "SELECT COUNT(*) FROM (SELECT EmployeeId, ShiftDate FROM ShiftEntries GROUP BY EmployeeId, ShiftDate HAVING COUNT(*) > 1) x"
$remaining = $cmdCheck.ExecuteScalar()

if ($remaining -eq 0) {
    Write-Host "  Verified: 0 duplicate pairs remain." -ForegroundColor Green
} else {
    Write-Host "  WARNING: $remaining duplicate pairs still exist!" -ForegroundColor Red
}

$conn.Close()
