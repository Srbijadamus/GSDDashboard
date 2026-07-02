$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

$upd = 0

function Fix($from, $to) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "UPDATE WicShiftEntries SET SupportLocation = @to WHERE SupportLocation = @from"
    $cmd.Parameters.AddWithValue("@from", $from) | Out-Null
    $cmd.Parameters.AddWithValue("@to",   $to)   | Out-Null
    $n = $cmd.ExecuteNonQuery()
    if ($n -gt 0) { Write-Host "  Updated $n rows: '$from' -> '$to'" -ForegroundColor Green }
    else          { Write-Host "  No rows for: '$from'" -ForegroundColor Gray }
    return $n
}

Write-Host "`nNormalizing WicShiftEntries.SupportLocation (all dates)..." -ForegroundColor Yellow

# Encoding corruption fix
$upd += Fix "Berlin - BrÃ¼ckenstrasse"  "Berlin - Brückenstrasse"

# Missing umlauts / punctuation
$upd += Fix "Berlin - Gaussstr"  "Berlin - Gaußstr."
$upd += Fix "Furstenwalde"       "Fürstenwalde"
$upd += Fix "Mulheim"            "Mülheim"
$upd += Fix "Munchen"            "München"
$upd += Fix "Munster"            "Münster"
$upd += Fix "Osnabruck"          "Osnabrück"
$upd += Fix "Saarbrucken"        "Saarbrücken"

# Name format differences
$upd += Fix "Essen BP1"          "Essen - BP1"
$upd += Fix "Essen TK1"          "Essen - TK"
$upd += Fix "Denbosch"           "s-Hertogenbosch"

$conn.Close()
Write-Host "`nTotal rows updated: $upd" -ForegroundColor Cyan
Write-Host "Non-WIC entries (Global Service Desk / Training / VWIC / blank) left unchanged." -ForegroundColor Gray
