# PS1_27_StadlandSubs.ps1
$uri = "http://localhost:5000/api/wic/substitutes?locationCode=DE~26935~Stadland~Dedesdorfer%20Stra%C3%9Fe%202&date=2026-06-19"

Write-Host ""
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "  Stadland substitutes - 2026-06-19" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "URL: $uri" -ForegroundColor DarkGray
Write-Host ""

try {
    $resp = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 30
    $json = $resp.Content | ConvertFrom-Json

    Write-Host "HTTP $($resp.StatusCode)" -ForegroundColor Green
    Write-Host ""
    Write-Host "LocationCode : $($json.locationCode)"
    Write-Host "DisplayName  : $($json.displayName)"
    Write-Host "Horizon      : $($json.horizon)"
    Write-Host "KnownNulls   : $($json.knownNulls -join ', ')"
    Write-Host ""

    if ($json.days -and $json.days.Count -gt 0) {
        $day = $json.days[0]
        Write-Host "--- Day: $($day.date) ($($day.dayOfWeek)) ---" -ForegroundColor Cyan
        Write-Host "  IsOpen        : $($day.isOpen)"
        Write-Host "  ClosedReason  : $($day.closedReason)"
        Write-Host "  Present       : $($day.present)"
        Write-Host "  Required      : $($day.required)"
        Write-Host "  Gap           : $($day.gap)"
        Write-Host "  CurrentStatus : $($day.currentStatus)"
        Write-Host "  Warning       : $($day.warning)"
        Write-Host "  BestPickId    : $($day.bestPickId)"
        Write-Host "  Candidates    : $($day.candidates.Count)"
        Write-Host ""

        if ($day.candidates.Count -gt 0) {
            Write-Host "  Top candidates:" -ForegroundColor Cyan
            $day.candidates | Select-Object -First 5 | ForEach-Object {
                Write-Host ("    {0,-25} src={1,-12} dist={2,6} km  reach={3}  score={4}" -f
                    $_.fullName, $_.sourceType,
                    $(if ($_.distanceKm) { [math]::Round($_.distanceKm,1) } else { "?" }),
                    $_.reachabilityTier,
                    [math]::Round($_.availabilityScore,2))
            }
        } else {
            Write-Host "  0 candidates returned." -ForegroundColor Red
        }
    } else {
        Write-Host "No days in response." -ForegroundColor Red
    }

    Write-Host ""
    Write-Host "--- Raw JSON ---" -ForegroundColor DarkGray
    $resp.Content | ConvertFrom-Json | ConvertTo-Json -Depth 8

} catch {
    $sc = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { "N/A" }
    Write-Host "HTTP $sc  ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        try {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            Write-Host $reader.ReadToEnd() -ForegroundColor Red
        } catch {}
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "  PS1_27 complete" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
