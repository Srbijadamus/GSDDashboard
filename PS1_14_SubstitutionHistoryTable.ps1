# PS1_14_SubstitutionHistoryTable.ps1
# Creates the SubstitutionHistory table if it does not exist.
# Idempotent -- safe to run multiple times.
# No here-strings. No Unicode.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Write-Host ""
Write-Host "=== PS1_14: SubstitutionHistory table ===" -ForegroundColor Yellow

# Check if table already exists
$checkSql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SubstitutionHistory'"

$exists = $false
try {
    $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $checkSql
    $cmd.CommandTimeout = 10
    $count = [int]$cmd.ExecuteScalar()
    $conn.Close()
    $exists = ($count -gt 0)
} catch {
    Write-Host "ERROR checking table: $_" -ForegroundColor Red
    exit 1
}

if ($exists) {
    Write-Host "Table SubstitutionHistory already exists. Skipping CREATE." -ForegroundColor Green
} else {
    Write-Host "Creating SubstitutionHistory table..." -ForegroundColor Cyan
    $createSql = "CREATE TABLE SubstitutionHistory (Id INT IDENTITY(1,1) PRIMARY KEY, EmployeeId NVARCHAR(20) NOT NULL, LocationCode NVARCHAR(100) NOT NULL, Date DATE NOT NULL, SourceType NVARCHAR(20) NULL, AssignedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE())"
    try {
        $conn2 = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn2.Open()
        $cmd2 = $conn2.CreateCommand()
        $cmd2.CommandText = $createSql
        $cmd2.CommandTimeout = 15
        $cmd2.ExecuteNonQuery() | Out-Null
        $conn2.Close()
        Write-Host "Table created successfully." -ForegroundColor Green
    } catch {
        Write-Host "ERROR creating table: $_" -ForegroundColor Red
        exit 1
    }

    # Create indexes
    $idx1 = "CREATE INDEX IX_SubHist_Emp ON SubstitutionHistory (EmployeeId)"
    $idx2 = "CREATE INDEX IX_SubHist_Date ON SubstitutionHistory (Date)"
    foreach ($idxSql in @($idx1, $idx2)) {
        try {
            $ci = New-Object System.Data.SqlClient.SqlConnection($cs)
            $ci.Open()
            $ci2 = $ci.CreateCommand()
            $ci2.CommandText = $idxSql
            $ci2.CommandTimeout = 10
            $ci2.ExecuteNonQuery() | Out-Null
            $ci.Close()
            Write-Host "Index created: $idxSql" -ForegroundColor DarkGreen
        } catch {
            Write-Host "WARNING creating index: $_" -ForegroundColor Yellow
        }
    }
}

# Verify
Write-Host ""
Write-Host ">>> Verify table exists and row count" -ForegroundColor Cyan
try {
    $conn3 = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn3.Open()
    $cmd3 = $conn3.CreateCommand()
    $cmd3.CommandText = "SELECT COUNT(*) FROM SubstitutionHistory"
    $cmd3.CommandTimeout = 10
    $rows = [int]$cmd3.ExecuteScalar()
    $conn3.Close()
    Write-Host "SubstitutionHistory row count: $rows" -ForegroundColor Green
} catch {
    Write-Host "ERROR querying SubstitutionHistory: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== PS1_14 complete ===" -ForegroundColor Green
