# PS1_RosterBackup.ps1
# Backs up the GSDDashboard database to C:\GSDDashboard\db-backups before the
# first roster write. Uses System.Data.SqlClient (no Invoke-Sqlcmd).
# No here-strings. No Unicode.

$ErrorActionPreference = "Stop"

$stamp  = Get-Date -Format "yyyyMMdd_HHmmss"
$dir    = "C:\GSDDashboard\db-backups"
$bakFile = Join-Path $dir ("GSDDashboard_" + $stamp + ".bak")

if (-not (Test-Path $dir)) { New-Item -Path $dir -ItemType Directory | Out-Null }

$cs = "Server=localhost\SQLEXPRESS;Database=master;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
try {
    $cmd = $conn.CreateCommand()
    $cmd.CommandTimeout = 300
    $cmd.CommandText = "BACKUP DATABASE [GSDDashboard] TO DISK = N'$bakFile' WITH INIT, CHECKSUM"
    $cmd.ExecuteNonQuery() | Out-Null
} finally {
    $conn.Close()
}

if (Test-Path $bakFile) {
    $size = (Get-Item $bakFile).Length
    Write-Host ("Backup OK: " + $bakFile + " (" + [math]::Round($size/1MB,1) + " MB)") -ForegroundColor Green
} else {
    Write-Host ("Backup FAILED: file not found: " + $bakFile) -ForegroundColor Red
    exit 1
}
