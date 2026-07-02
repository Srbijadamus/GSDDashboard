$r = Invoke-RestMethod -Uri "http://localhost:5000/api/wic/forecast?horizon=7"
$essen = $r.locations | Where-Object { $_.locationCode -like "*Essen*ThyssenKrupp*" }
if (-not $essen) {
    Write-Host "Essen-TK not found in forecast response!" -ForegroundColor Red
    Write-Host "All location codes:" -ForegroundColor Yellow
    $r.locations | ForEach-Object { Write-Host "  $($_.locationCode) | $($_.todayStatus)" }
} else {
    Write-Host "=== Essen-TK Forecast ===" -ForegroundColor Yellow
    Write-Host "todayStatus: $($essen.todayStatus)"
    $today = $essen.forecast | Where-Object { $_.date -eq (Get-Date -Format "yyyy-MM-dd") }
    Write-Host "--- Today entry ---"
    Write-Host "  status:            $($today.status)"
    Write-Host "  scheduledCount:    $($today.scheduledCount)"
    Write-Host "  effectiveCoverage: $($today.effectiveCoverage)"
    Write-Host "  minRequired:       $($today.minRequired)"
    Write-Host "  coverageBuffer:    $($today.coverageBuffer)"
    Write-Host "  isAtRisk:          $($today.isAtRisk)"
}

Write-Host ""
Write-Host "=== All locations todayStatus summary ===" -ForegroundColor Yellow
$r.locations | Select-Object locationCode, todayStatus | Format-Table -AutoSize
