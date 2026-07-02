$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

# Find Essen-TK location code
Write-Host "`n=== Essen WIC locations ===" -ForegroundColor Yellow
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT LocationCode, LocationCodeLegacy, DisplayName, Coordinates, IsActive FROM WicLocations WHERE DisplayName LIKE '%Essen%' OR DisplayName LIKE '%TK%' ORDER BY DisplayName"
$reader = $cmd.ExecuteReader()
$rows = @()
while ($reader.Read()) {
    $row = [ordered]@{}
    for ($i = 0; $i -lt $reader.FieldCount; $i++) { $row[$reader.GetName($i)] = $reader.GetValue($i) }
    $rows += [PSCustomObject]$row
}
$reader.Close()
$rows | Format-Table -AutoSize

$essenCode = $rows | Where-Object { $_.IsActive -eq $true } | Select-Object -First 1 -ExpandProperty LocationCode
$conn.Close()

if (-not $essenCode) {
    Write-Host "No active Essen location found!" -ForegroundColor Red
    exit
}

Write-Host "`n=== Testing substitutes for: $essenCode ===" -ForegroundColor Yellow
$url = "http://localhost:5000/api/wic/substitutes?locationCode=$([Uri]::EscapeDataString($essenCode))&date=2026-06-22"
Write-Host "GET $url" -ForegroundColor Cyan

try {
    $resp = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 15
    $day  = $resp.days | Select-Object -First 1

    Write-Host "`n--- Day result ---" -ForegroundColor Green
    Write-Host "Date         : $($day.date)"
    Write-Host "Status       : $($day.currentStatus)"
    Write-Host "Present/Min  : $($day.presentCount) / $($day.minRequired)"
    Write-Host "Gap          : $($day.gap)"
    Write-Host "Candidates   : $($day.candidates.Count)"
    Write-Host "Best pick    : $($day.bestPickId)"
    Write-Host "Warning      : $($day.warning)"

    if ($day.candidates.Count -gt 0) {
        Write-Host "`n--- Top 10 candidates ---" -ForegroundColor Green
        $day.candidates | Select-Object -First 10 | ForEach-Object {
            Write-Host ("  [{0}] {1,-28} src={2,-8} dist={3,6:N0}km tier={4,-12} avail={5}" -f
                $_.employeeId, $_.fullName, $_.sourceType,
                ($_.distanceKm ?? 0), ($_.reachabilityTier ?? "?"), $_.availabilityType)
        }
    }
} catch {
    Write-Host "API call failed: $_" -ForegroundColor Red
    Write-Host "Is the backend running on port 5000?" -ForegroundColor Yellow
}
