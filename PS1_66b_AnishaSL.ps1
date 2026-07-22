# PS1_66b_AnishaSL.ps1
# Inserts Anisha Nellikka Panikkan SL for 2026-07-14.
# Source: Email received 2026-07-14 -- AU electronically submitted.

$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
$TODAY   = "2026-07-14"

Write-Host ""
Write-Host "=== PS1_66b: Anisha Nellikka Panikkan -- SL $TODAY ===" -ForegroundColor Yellow
Write-Host ""

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
try { $conn.Open(); Write-Host "DB: OK" -ForegroundColor Green }
catch { Write-Host "ERROR: $_" -ForegroundColor Red; exit 1 }

# Check if already inserted
$checkSql = @"
SELECT COUNT(*) FROM SickLeaves
WHERE EmployeeId = (SELECT TOP 1 EmployeeId FROM Employees WHERE FullName LIKE 'Anisha Nellikka Panikkan')
  AND FirstDay = '$TODAY'
"@
$checkCmd = New-Object System.Data.SqlClient.SqlCommand($checkSql, $conn)
$exists   = [int]$checkCmd.ExecuteScalar()

if ($exists -gt 0) {
    Write-Host "  ALREADY EXISTS -- Anisha Nellikka Panikkan already has SL for $TODAY" -ForegroundColor Yellow
    Write-Host "  No insert needed." -ForegroundColor Yellow
    $conn.Close()
    Write-Host ""
    Write-Host "=== PS1_66b complete (no action needed) ===" -ForegroundColor Green
    exit 0
}

# Lookup employee
$empSql = "SELECT TOP 1 EmployeeId, FirstName, LastName, TeamLeadName FROM Employees WHERE FullName LIKE 'Anisha Nellikka Panikkan'"
$empCmd = New-Object System.Data.SqlClient.SqlCommand($empSql, $conn)
$er = $empCmd.ExecuteReader()
if (-not $er.Read()) {
    Write-Host "ERROR: Anisha Nellikka Panikkan not found in Employees." -ForegroundColor Red
    $er.Close(); $conn.Close(); exit 1
}
$empId       = [string]$er["EmployeeId"]
$firstName   = [string]$er["FirstName"]
$lastName    = [string]$er["LastName"]
$teamLead    = [string]$er["TeamLeadName"]
$er.Close()

Write-Host ("  Found: EmployeeId={0}  Name={1} {2}  TL={3}" -f $empId, $firstName, $lastName, $teamLead) -ForegroundColor Cyan

# Insert
$insertSql = @"
INSERT INTO SickLeaves
    (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, CreatedAt)
VALUES
    (@EmployeeId, @FirstName, @LastName, @TeamLeadName, @FirstDay, @LastDay, 1, 'SL', @CreatedAt)
"@
$fd = [datetime]::Parse($TODAY)

$insCmd = New-Object System.Data.SqlClient.SqlCommand($insertSql, $conn)
$insCmd.Parameters.AddWithValue("@EmployeeId",   $empId)               | Out-Null
$insCmd.Parameters.AddWithValue("@FirstName",    $firstName)           | Out-Null
$insCmd.Parameters.AddWithValue("@LastName",     $lastName)            | Out-Null
$insCmd.Parameters.AddWithValue("@TeamLeadName", $teamLead)            | Out-Null
$insCmd.Parameters.AddWithValue("@FirstDay",     $fd)                  | Out-Null
$insCmd.Parameters.AddWithValue("@LastDay",      $fd)                  | Out-Null
$insCmd.Parameters.AddWithValue("@CreatedAt",    [datetime]::UtcNow)   | Out-Null

try {
    $insCmd.ExecuteNonQuery() | Out-Null
    Write-Host "  INSERT OK -- Anisha Nellikka Panikkan  SL  $TODAY" -ForegroundColor Green
} catch {
    Write-Host ("  ERROR: {0}" -f $_) -ForegroundColor Red
}

$conn.Close()
Write-Host ""
Write-Host "  Note: AU electronisch eingereicht (2026-07-14)." -ForegroundColor DarkCyan
Write-Host "  If AU covers multiple days, re-run PS1_66b with updated LastDay." -ForegroundColor DarkCyan
Write-Host ""
Write-Host "=== PS1_66b complete ===" -ForegroundColor Green
