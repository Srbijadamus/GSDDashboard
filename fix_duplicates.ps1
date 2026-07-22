#Requires -Version 7.0
$conn = New-Object System.Data.SqlClient.SqlConnection "Server=localhost\SQLEXPRESS;Database=ShiftKioskDB;Integrated Security=true;TrustServerCertificate=true;"
$conn.Open()

# Deaktiviraj 5 visak redova (stari duplikati + Jens Dotsch)
$toDeactivate = @(
    @{ id = 214; reason = "Duplikat — stari 'Aman Kedo' (ispravan id=188 'Amani Kedo')" },
    @{ id = 273; reason = "Jens Dotsch — nije na aktuelnoj listi 120" },
    @{ id = 274; reason = "Duplikat — stari 'Onder Arslan' bez umlauta (ispravan id=268)" },
    @{ id = 276; reason = "Duplikat — stari 'Sebastian Hoeck' bez umlauta (ispravan id=255)" },
    @{ id = 277; reason = "Duplikat — stari 'Kai Eric Kumlehn' (ispravan id=278 'Kai Erik')" }
)

foreach ($a in $toDeactivate) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "UPDATE agents SET active=0 WHERE id=@id"
    $cmd.Parameters.AddWithValue('@id', $a.id) | Out-Null
    $rows = $cmd.ExecuteNonQuery()
    Write-Host ("  [OK]  id={0,-5}  deaktiviran  ({1})" -f $a.id, $a.reason) -ForegroundColor Green
}

# Finalni broj
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT COUNT(*) FROM agents WHERE active=1"
$n = $cmd2.ExecuteScalar()
$conn.Close()

Write-Host ""
$color = if ($n -eq 120) { 'Green' } else { 'Yellow' }
Write-Host ("  Aktivnih agenata: {0}  (ocekivano: 120)" -f $n) -ForegroundColor $color
