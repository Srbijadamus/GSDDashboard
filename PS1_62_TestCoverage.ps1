# Forecast always starts today. Use horizon=30 to cover all target dates.
# 2026-06-22 is YESTERDAY - not in forecast; skipped with a note.

$targetDates = @("2026-06-30", "2026-07-06", "2026-07-15")
$today = (Get-Date).ToString("yyyy-MM-dd")

Write-Host "=== Coverage forecast (generated from $today, horizon=30) ===" -ForegroundColor Cyan
Write-Host "Note: 2026-06-22 is in the past - not available from this endpoint." -ForegroundColor DarkYellow
Write-Host ""

$r = Invoke-RestMethod -Uri "http://localhost:5000/api/wic/forecast?horizon=30"
Write-Host "Locations in response: $($r.locationCount)" -ForegroundColor Gray

foreach ($targetDate in $targetDates) {
    Write-Host ""
    Write-Host "=== Date: $targetDate ===" -ForegroundColor Yellow

    $covered   = 0
    $partial   = 0
    $uncovered = 0
    $closed    = 0
    $missing   = 0
    $detail    = @()

    foreach ($loc in $r.locations) {
        $day = $loc.forecast | Where-Object { $_.date -eq $targetDate }
        if (-not $day) {
            $missing++
            continue
        }
        switch ($day.status) {
            "COVERED"   { $covered++;   }
            "PARTIAL"   { $partial++;   $detail += "$($loc.displayName): eff=$($day.effectiveCoverage) min=$($day.minRequired)" }
            "UNCOVERED" { $uncovered++; $detail += "$($loc.displayName): eff=$($day.effectiveCoverage) min=$($day.minRequired)" }
            "CLOSED"    { $closed++;    }
            default     { $missing++;   }
        }
    }

    Write-Host "  COVERED:   $covered"
    Write-Host "  PARTIAL:   $partial"
    Write-Host "  UNCOVERED: $uncovered" -ForegroundColor $(if ($uncovered -gt 0) { "Red" } else { "Gray" })
    Write-Host "  CLOSED:    $closed"
    if ($missing -gt 0) { Write-Host "  (missing/no entry: $missing)" -ForegroundColor DarkGray }

    if ($detail.Count -gt 0) {
        Write-Host "  At-risk locations:" -ForegroundColor Red
        foreach ($d in $detail) { Write-Host "    - $d" }
    }
}

Write-Host ""
Write-Host "=== Today's status summary (todayStatus per location) ===" -ForegroundColor Yellow
$r.locations | Group-Object todayStatus | Sort-Object Name | ForEach-Object {
    $color = switch ($_.Name) {
        "COVERED"   { "Green" }
        "PARTIAL"   { "DarkYellow" }
        "UNCOVERED" { "Red" }
        default     { "Gray" }
    }
    Write-Host "  $($_.Name): $($_.Count)" -ForegroundColor $color
}

Write-Host ""
Write-Host "=== UNCOVERED/PARTIAL locations today ===" -ForegroundColor Yellow
$atRisk = $r.locations | Where-Object { $_.todayStatus -eq "UNCOVERED" -or $_.todayStatus -eq "PARTIAL" }
if ($atRisk.Count -eq 0) {
    Write-Host "  None - all open locations covered today." -ForegroundColor Green
} else {
    foreach ($loc in $atRisk) {
        $todayEntry = $loc.forecast | Where-Object { $_.date -eq $today }
        $eff = if ($todayEntry) { $todayEntry.effectiveCoverage } else { "?" }
        $min = if ($todayEntry) { $todayEntry.minRequired } else { "?" }
        Write-Host "  $($loc.displayName) [$($loc.todayStatus)] eff=$eff min=$min"
    }
}
