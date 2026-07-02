Add-Type -AssemblyName System.Web
$code = [System.Web.HttpUtility]::UrlEncode("DE~45143~Essen~ThyssenKrupp Allee 1")
$url = "http://localhost:5000/api/wic/substitutes?locationCode=$code&date=2026-06-22"
Write-Host "GET $url" -ForegroundColor Cyan
$r = Invoke-RestMethod -Uri $url
$d = $r.days[0]
Write-Host "Status:     $($d.currentStatus)"
Write-Host "Present:    $($d.presentCount)"
Write-Host "Required:   $($d.minRequired)"
Write-Host "Gap:        $($d.gap)"
Write-Host "Candidates: $($d.candidates.Count)"
Write-Host "Best pick:  $($d.bestPickId)"
Write-Host "Warning:    $($d.warning)"
Write-Host ""
if ($d.candidates.Count -gt 0) {
    Write-Host "Top 10 candidates:" -ForegroundColor Green
    $d.candidates | Select-Object -First 10 | ForEach-Object {
        Write-Host "  $($_.fullName) | src=$($_.sourceType) | $($_.distanceKm)km | avail=$($_.availabilityType)"
    }
} else {
    Write-Host "NO candidates returned." -ForegroundColor Red
}
