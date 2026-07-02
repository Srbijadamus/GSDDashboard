$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT LocationCode FROM WicLocations WHERE DisplayName = 'Essen - TK'"
$code = $cmd.ExecuteScalar()
$conn.Close()

Write-Host "Essen-TK LocationCode: $code"

$url = "http://localhost:5000/api/wic/substitutes?locationCode=$code&date=2026-06-22"
Write-Host "GET $url"
$resp = Invoke-RestMethod -Uri $url
$day = $resp.days[0]

Write-Host "Status: $($day.currentStatus) | Present: $($day.presentCount) / $($day.minRequired) | Gap: $($day.gap)"
Write-Host "Candidates for Essen-TK:"
$day.candidates | ForEach-Object {
    Write-Host "  $($_.fullName) | $($_.sourceType) | dist=$($_.distanceKm)km | avail=$($_.availabilityType)"
}
Write-Host "Total candidates: $($day.candidates.Count)"
Write-Host "Best pick: $($day.bestPickId)"
Write-Host "Warning: $($day.warning)"
