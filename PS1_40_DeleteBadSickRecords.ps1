# PS1_40_DeleteBadSickRecords.ps1 - Delete bad/duplicate SickLeave records
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

function Run-Delete($label, $sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
    $rows = $cmd.ExecuteNonQuery()
    Write-Host "  $label - deleted $rows row(s)" -ForegroundColor Green
}

Write-Host "`nDeleting bad SickLeave records..." -ForegroundColor Cyan

# FIX 1: Sebastian Hock 365-day garbage record (2026-01-01 to 2026-12-31)
Run-Delete "FIX 1 - Sebastian Hock bad record (Id 193)" `
    "DELETE FROM SickLeaves WHERE Id = 193"

# FIX 2: Duplicate single-day 22.06 records superseded by longer periods
Run-Delete "FIX 2a - Pascal Dutz duplicate (Id 232)" `
    "DELETE FROM SickLeaves WHERE Id = 232"
Run-Delete "FIX 2b - Anil Bedzeti duplicate (Id 231)" `
    "DELETE FROM SickLeaves WHERE Id = 231"

$conn.Close()
Write-Host "`nDone." -ForegroundColor Cyan
