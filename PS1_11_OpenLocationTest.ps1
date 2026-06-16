# PS1_11_OpenLocationTest.ps1
# Finds the first WIC location that is open today, then calls
# /api/wic/substitutes on it and prints the full JSON response.
# No here-strings. No Unicode. Run from powershell.exe 5.1.

$baseUrl  = "http://localhost:5000"
$testDate = "2026-06-15"
$cs       = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

# --- Try Dortmund first ------------------------------------------------------

Write-Host ""
Write-Host "--- Trying Dortmund directly ---"
$dortmundCode = "DE~44139~Dortmund"
$urlD = "$baseUrl/api/wic/substitutes?locationCode=$dortmundCode&date=$testDate&horizon=1"
$dortmundOk = $false

try {
    $subD = Invoke-RestMethod -Uri $urlD -Method GET -TimeoutSec 15
    if ($subD.days -and $subD.days.Count -gt 0 -and $subD.days[0].isOpen -eq $true) {
        Write-Host "Dortmund is OPEN today - using it." -ForegroundColor Green
        $dortmundOk = $true
        Write-Host ""
        Write-Host "Full JSON response (Dortmund):" -ForegroundColor Cyan
        Write-Host ($subD | ConvertTo-Json -Depth 10)
        Write-Host ""
        Write-Host "Summary:" -ForegroundColor Cyan
        Write-Host "  LocationCode : $($subD.locationCode)"
        Write-Host "  DisplayName  : $($subD.displayName)"
        $day0 = $subD.days[0]
        Write-Host "  Date         : $($day0.date)"
        Write-Host "  Status       : $($day0.currentStatus)"
        Write-Host "  Present      : $($day0.present)"
        Write-Host "  Gap          : $($day0.gap)"
        Write-Host "  Candidates   : $($day0.candidates.Count)"
        if ($day0.candidates.Count -gt 0) {
            $top = $day0.candidates[0]
            Write-Host "  Top candidate: $($top.fullName) [$($top.sourceType)] tier=$($top.reachabilityTier) dist=$($top.distanceKm)km"
        }
    } else {
        Write-Host "Dortmund is CLOSED today - searching for open location." -ForegroundColor Yellow
    }
} catch {
    Write-Host "Dortmund request failed: $_" -ForegroundColor Red
}

# --- If Dortmund closed, find first open location from /api/wic/open ---------

if (-not $dortmundOk) {
    Write-Host ""
    Write-Host "--- GET /api/wic/open?date=$testDate&horizon=1 ---"
    $openCode = $null
    try {
        $openList = Invoke-RestMethod -Uri "$baseUrl/api/wic/open?date=$testDate&horizon=1" -Method GET -TimeoutSec 15
        $firstOpen = $openList | Where-Object { $_.isOpen -eq $true } | Select-Object -First 1
        if ($firstOpen) {
            $openCode = $firstOpen.locationCode
            Write-Host "First open location: $openCode ($($firstOpen.displayName))" -ForegroundColor Cyan
        } else {
            Write-Host "No open locations returned - checking cards endpoint instead." -ForegroundColor Yellow
        }
    } catch {
        Write-Host "/api/wic/open failed: $_" -ForegroundColor Red
    }

    # Fallback: use /api/wic/cards if /api/wic/open does not exist
    if (-not $openCode) {
        Write-Host ""
        Write-Host "--- Fallback: GET /api/wic/cards?date=$testDate ---"
        try {
            $cards = Invoke-RestMethod -Uri "$baseUrl/api/wic/cards?date=$testDate" -Method GET -TimeoutSec 15
            $firstCard = $cards | Where-Object { $_.todaySchedule.isClosed -eq $false } | Select-Object -First 1
            if ($firstCard) {
                $openCode = $firstCard.locationCode
                Write-Host "First open location (via cards): $openCode ($($firstCard.displayName))" -ForegroundColor Cyan
            } else {
                Write-Host "No open locations found via cards either." -ForegroundColor Yellow
            }
        } catch {
            Write-Host "Cards fallback failed: $_" -ForegroundColor Red
        }
    }

    # --- Test substitutes on the open location --------------------------------
    if ($openCode) {
        Write-Host ""
        $urlS = "$baseUrl/api/wic/substitutes?locationCode=$openCode&date=$testDate&horizon=1"
        Write-Host "--- GET /api/wic/substitutes for $openCode ---"
        Write-Host "URL: $urlS"
        try {
            $sub = Invoke-RestMethod -Uri $urlS -Method GET -TimeoutSec 20
            Write-Host ""
            Write-Host "Full JSON response:" -ForegroundColor Cyan
            Write-Host ($sub | ConvertTo-Json -Depth 10)
            Write-Host ""
            Write-Host "Summary:" -ForegroundColor Cyan
            Write-Host "  LocationCode : $($sub.locationCode)"
            Write-Host "  DisplayName  : $($sub.displayName)"
            if ($sub.days -and $sub.days.Count -gt 0) {
                $day0 = $sub.days[0]
                Write-Host "  Date         : $($day0.date)"
                Write-Host "  IsOpen       : $($day0.isOpen)"
                Write-Host "  Status       : $($day0.currentStatus)"
                Write-Host "  Present      : $($day0.present)"
                Write-Host "  Required     : $($day0.required)"
                Write-Host "  Gap          : $($day0.gap)"
                Write-Host "  Candidates   : $($day0.candidates.Count)"
                if ($day0.candidates.Count -gt 0) {
                    $top = $day0.candidates[0]
                    Write-Host "  Top candidate: $($top.fullName) [$($top.sourceType)] tier=$($top.reachabilityTier) dist=$($top.distanceKm)km"
                }
                if ($day0.warning) {
                    Write-Host "  Warning      : $($day0.warning)" -ForegroundColor Yellow
                }
            }
            Write-Host ""
            Write-Host "Substitutes endpoint confirmed working on open location." -ForegroundColor Green
        } catch {
            Write-Host "ERROR calling substitutes: $_" -ForegroundColor Red
        }
    } else {
        Write-Host "Could not find any open location to test against." -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== PS1_11 complete ===" -ForegroundColor Green
