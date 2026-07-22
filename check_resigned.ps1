$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"

$resignedIds = @(
    '9120968','9120979','9083015','9043574','9126816','9126879','9122677',
    '9074584','4451025','4451022','4451014','4451020','3193178','3193180'
)

$idList = ($resignedIds | ForEach-Object { "'" + $_ + "'" }) -join ","

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()

Write-Host ""
Write-Host "=== Verify: Resigned Employees Cleanup ===" -ForegroundColor Yellow
Write-Host ""

# 1. Find ALL tables with EmployeeId
$tablesSql = @"
SELECT t.TABLE_NAME FROM INFORMATION_SCHEMA.COLUMNS c
JOIN INFORMATION_SCHEMA.TABLES t ON c.TABLE_NAME = t.TABLE_NAME
WHERE c.COLUMN_NAME = 'EmployeeId' AND t.TABLE_TYPE = 'BASE TABLE'
ORDER BY t.TABLE_NAME
"@
$tablesCmd = New-Object System.Data.SqlClient.SqlCommand($tablesSql, $conn)
$tr = $tablesCmd.ExecuteReader()
$tables = @()
while ($tr.Read()) { $tables += [string]$tr["TABLE_NAME"] }
$tr.Close()

$totalRemaining = 0
$clean = $true

foreach ($table in $tables) {
    $sql = "SELECT COUNT(*) FROM [$table] WHERE EmployeeId IN ($idList)"
    $cmd = New-Object System.Data.SqlClient.SqlCommand($sql, $conn)
    $cnt = [int]$cmd.ExecuteScalar()
    $totalRemaining += $cnt
    if ($cnt -gt 0) {
        Write-Host ("  [OSTALO]  {0,-35}  {1} rows" -f $table, $cnt) -ForegroundColor Red
        $clean = $false
    } else {
        Write-Host ("  [OK]      {0,-35}  0 rows" -f $table) -ForegroundColor Green
    }
}

# 2. Check by full name in case EmployeeId differs (name-based check)
Write-Host ""
Write-Host "--- Name-based check in Employees ---" -ForegroundColor Cyan
$names = @(
    'Daniel Danso','Akhilesh Ramesh','Frederik Van Eeckhoven','Antonio Fiorito',
    'Walfredo Wester','Elvin Delic','Dennis Obazee','Teresa Kwasniewska',
    'Benjamin Bitz','Marcel Marc Bronheim','Stefan Burgdorf','Ivonne Specht',
    'Samantha Buys','Cortneigh Halim'
)
$nameList = ($names | ForEach-Object { "'" + $_.Replace("'","''") + "'" }) -join ","
$nameSql  = "SELECT EmployeeId, FullName FROM Employees WHERE FullName IN ($nameList)"
$nameCmd  = New-Object System.Data.SqlClient.SqlCommand($nameSql, $conn)
$nr = $nameCmd.ExecuteReader()
$nameFound = @()
while ($nr.Read()) { $nameFound += ("{0}  {1}" -f [string]$nr["EmployeeId"], [string]$nr["FullName"]) }
$nr.Close()

if ($nameFound.Count -gt 0) {
    Write-Host ("  [OSTALO]  {0} person(s) still in Employees by name:" -f $nameFound.Count) -ForegroundColor Red
    $nameFound | ForEach-Object { Write-Host ("    $_") -ForegroundColor Red }
    $clean = $false
} else {
    Write-Host "  [OK]  None of the 14 resigned agents found by name." -ForegroundColor Green
}

$conn.Close()

Write-Host ""
if ($clean) {
    Write-Host "=== RESULT: CLEAN -- sve je obrisano iz sistema ===" -ForegroundColor Green
} else {
    Write-Host ("=== RESULT: NIJE CISTO -- {0} rows ostalo u sistemu ===" -f $totalRemaining) -ForegroundColor Red
}
Write-Host ""
