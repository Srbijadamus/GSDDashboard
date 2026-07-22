#Requires -Version 7.0
$CONNSTR = "Server=localhost\SQLEXPRESS;Database=ShiftKioskDB;Integrated Security=true;TrustServerCertificate=true;"
$conn = New-Object System.Data.SqlClient.SqlConnection $CONNSTR
$conn.Open()

$name = [char]0xD6 + "nder Arslan"   # Önder Arslan

$agId = $null
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT id FROM agents WHERE full_name=@fn AND active=1"
$cmd.Parameters.AddWithValue('@fn', $name) | Out-Null
$r = $cmd.ExecuteScalar()
if ($null -ne $r -and $r -isnot [System.DBNull]) { $agId = $r }

if (-not $agId) { Write-Host "Agent nije nadjen: $name" -ForegroundColor Red; $conn.Close(); return }

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT id FROM wic_centers WHERE city='Hamburg' AND active=1"
$wcId = $cmd2.ExecuteScalar()

$cmd3 = $conn.CreateCommand()
$cmd3.CommandText = "SELECT COUNT(*) FROM wic_agent_roles WHERE wic_center_id=@wc AND agent_id=@ag AND role='BACKUP'"
$cmd3.Parameters.AddWithValue('@wc', $wcId) | Out-Null
$cmd3.Parameters.AddWithValue('@ag', $agId) | Out-Null
$exists = $cmd3.ExecuteScalar()

if ($exists -gt 0) {
    Write-Host "Vec postoji" -ForegroundColor Yellow
} else {
    $cmd4 = $conn.CreateCommand()
    $cmd4.CommandText = "INSERT INTO wic_agent_roles (wic_center_id, agent_id, role) VALUES (@wc, @ag, 'BACKUP')"
    $cmd4.Parameters.AddWithValue('@wc', $wcId) | Out-Null
    $cmd4.Parameters.AddWithValue('@ag', $agId) | Out-Null
    $cmd4.ExecuteNonQuery() | Out-Null
    Write-Host "[OK]  Hamburg BACKUP: $name" -ForegroundColor Green
}

$conn.Close()
