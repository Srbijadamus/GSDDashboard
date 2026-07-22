# PS1_70_HardDeleteResigned.ps1
# Hard-deletes 14 resigned employees from all tables.
# Set $DRY_RUN = $true to preview only (no deletes).

$DRY_RUN = $false

$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"

$resignedIds = @(
    '9120968',  # Daniel Danso
    '9120979',  # Akhilesh Ramesh
    '9083015',  # Frederik Van Eeckhoven
    '9043574',  # Antonio Fiorito
    '9126816',  # Walfredo Wester
    '9126879',  # Elvin Delic
    '9122677',  # Dennis Obazee
    '9074584',  # Teresa Kwasniewska
    '4451025',  # Benjamin Bitz
    '4451022',  # Marcel Marc Bronheim
    '4451014',  # Stefan Burgdorf
    '4451020',  # Ivonne Specht
    '3193178',  # Samantha Buys
    '3193180'   # Cortneigh Halim
)

$idList = ($resignedIds | ForEach-Object { "'" + $_ + "'" }) -join ","

Write-Host ""
Write-Host "=== PS1_70: Hard Delete Resigned Employees ===" -ForegroundColor $(if ($DRY_RUN) { "Cyan" } else { "Red" })
Write-Host "    DRY_RUN = $DRY_RUN  |  Employees to delete: $($resignedIds.Count)" -ForegroundColor Yellow
Write-Host ""

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
try { $conn.Open(); Write-Host "DB: OK" -ForegroundColor Green }
catch { Write-Host "ERROR: $_" -ForegroundColor Red; exit 1 }

# ── Step 1: Find all tables with EmployeeId column ────────────────────────────

Write-Host ""
Write-Host "--- Step 1: Tables with EmployeeId column ---" -ForegroundColor Cyan

$tablesSql = @"
SELECT t.TABLE_NAME
FROM INFORMATION_SCHEMA.COLUMNS c
JOIN INFORMATION_SCHEMA.TABLES t ON c.TABLE_NAME = t.TABLE_NAME
WHERE c.COLUMN_NAME = 'EmployeeId'
  AND t.TABLE_TYPE = 'BASE TABLE'
ORDER BY t.TABLE_NAME
"@
$tablesCmd = New-Object System.Data.SqlClient.SqlCommand($tablesSql, $conn)
$tr        = $tablesCmd.ExecuteReader()
$tables    = @()
while ($tr.Read()) { $tables += [string]$tr["TABLE_NAME"] }
$tr.Close()

Write-Host ("  Found {0} tables: {1}" -f $tables.Count, ($tables -join ", ")) -ForegroundColor Cyan

# ── Step 2: Preview row counts ────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Step 2: Row counts to be deleted ---" -ForegroundColor Cyan

$totalRows = 0
foreach ($table in $tables) {
    $cntSql = "SELECT COUNT(*) FROM [$table] WHERE EmployeeId IN ($idList)"
    $cntCmd = New-Object System.Data.SqlClient.SqlCommand($cntSql, $conn)
    $cnt    = [int]$cntCmd.ExecuteScalar()
    $totalRows += $cnt
    $color  = if ($cnt -gt 0) { "Yellow" } else { "DarkGray" }
    Write-Host ("  {0,-35}  {1} rows" -f $table, $cnt) -ForegroundColor $color
}

Write-Host ""
Write-Host ("  TOTAL rows to delete: {0}" -f $totalRows) -ForegroundColor $(if ($totalRows -gt 0) { "Yellow" } else { "Green" })

# Verify employees exist
$empCheckSql = "SELECT EmployeeId, FullName FROM Employees WHERE EmployeeId IN ($idList)"
$empCheckCmd = New-Object System.Data.SqlClient.SqlCommand($empCheckSql, $conn)
$ecr         = $empCheckCmd.ExecuteReader()
Write-Host ""
Write-Host "  Employees confirmed in DB:" -ForegroundColor Cyan
$foundIds = @()
while ($ecr.Read()) {
    Write-Host ("    {0}  {1}" -f [string]$ecr["EmployeeId"], [string]$ecr["FullName"]) -ForegroundColor White
    $foundIds += [string]$ecr["EmployeeId"]
}
$ecr.Close()

$notFound = $resignedIds | Where-Object { $_ -notin $foundIds }
if ($notFound) {
    Write-Host ""
    Write-Host "  NOT FOUND in Employees (already deleted or wrong ID):" -ForegroundColor Yellow
    foreach ($id in $notFound) { Write-Host ("    {0}" -f $id) -ForegroundColor DarkYellow }
}

if ($DRY_RUN) {
    Write-Host ""
    Write-Host "=== DRY RUN complete -- no changes made ===" -ForegroundColor Cyan
    Write-Host "    Set `$DRY_RUN = `$false to execute." -ForegroundColor Cyan
    $conn.Close()
    exit 0
}

# ── Step 3: DELETE (child tables first, Employees last) ───────────────────────

Write-Host ""
Write-Host "--- Step 3: DELETE ---" -ForegroundColor Red

# Delete from all tables except Employees first
$childTables = $tables | Where-Object { $_ -ne "Employees" }

foreach ($table in $childTables) {
    try {
        $delSql = "DELETE FROM [$table] WHERE EmployeeId IN ($idList)"
        $delCmd = New-Object System.Data.SqlClient.SqlCommand($delSql, $conn)
        $rows   = $delCmd.ExecuteNonQuery()
        Write-Host ("  DELETE  {0,-35}  {1} rows" -f $table, $rows) -ForegroundColor $(if ($rows -gt 0) { "Green" } else { "DarkGray" })
    } catch {
        Write-Host ("  ERROR   {0,-35}  {1}" -f $table, $_) -ForegroundColor Red
    }
}

# Delete from Employees last
try {
    $delEmpSql = "DELETE FROM [Employees] WHERE EmployeeId IN ($idList)"
    $delEmpCmd = New-Object System.Data.SqlClient.SqlCommand($delEmpSql, $conn)
    $empRows   = $delEmpCmd.ExecuteNonQuery()
    Write-Host ("  DELETE  {0,-35}  {1} rows" -f "Employees", $empRows) -ForegroundColor $(if ($empRows -gt 0) { "Green" } else { "Yellow" })
} catch {
    Write-Host ("  ERROR   Employees  {0}" -f $_) -ForegroundColor Red
}

# ── Step 4: Verify ────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Step 4: Verify ---" -ForegroundColor Cyan

$verSql = "SELECT COUNT(*) FROM Employees WHERE EmployeeId IN ($idList)"
$verCmd = New-Object System.Data.SqlClient.SqlCommand($verSql, $conn)
$remaining = [int]$verCmd.ExecuteScalar()

if ($remaining -eq 0) {
    Write-Host "  All 14 employees removed from Employees table." -ForegroundColor Green
} else {
    Write-Host ("  WARNING: {0} employee(s) still remain in Employees!" -f $remaining) -ForegroundColor Red
}

$totalSql = "SELECT COUNT(*) FROM Employees WHERE IsActive = 1"
$totalCmd = New-Object System.Data.SqlClient.SqlCommand($totalSql, $conn)
$newTotal = [int]$totalCmd.ExecuteScalar()
Write-Host ("  Active employees remaining: {0}  (was 127)" -f $newTotal) -ForegroundColor Cyan

$conn.Close()

Write-Host ""
Write-Host "=== PS1_70 complete ===" -ForegroundColor Green
