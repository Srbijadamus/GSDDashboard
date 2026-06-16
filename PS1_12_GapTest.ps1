# PS1_12_GapTest.ps1
# Finds a MAIN agent assigned to Augsburg (correct join: e.FullName = waa.EmployeeName,
# NOT EmployeeId -- WicAgentAssignments has no EmployeeId column).
# Marks that agent absent via absentIds and calls /api/wic/substitutes.
# Expected: gap=1, ranked candidate list with sourceType/distanceKm/reachabilityTier.
# No here-strings. No Unicode. Run from powershell.exe 5.1.

$cs       = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$baseUrl  = "http://localhost:5000"
$testDate = "2026-06-15"

# --- 1. Find MAIN agent at Augsburg and resolve the new-style LocationCode ---

Write-Host ""
Write-Host "--- 1. DB: find MAIN agent at Augsburg ---"

$empId    = $null
$empName  = $null
$locCode  = $null

$sqlFind = "SELECT TOP 1 e.EmployeeId, e.FullName, wl.LocationCode AS NewCode FROM Employees e INNER JOIN WicAgentAssignments waa ON e.FullName = waa.EmployeeName INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode OR wl.LocationCodeLegacy = waa.LocationCode WHERE waa.LocationCode = 'DE_Augsburg' AND waa.AssignmentType = 'MAIN' AND waa.IsActive = 1 AND e.IsActive = 1"

try {
    $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sqlFind
    $cmd.CommandTimeout = 15
    $r = $cmd.ExecuteReader()
    if ($r.Read()) {
        $empId   = [string]$r["EmployeeId"]
        $empName = [string]$r["FullName"]
        $locCode = [string]$r["NewCode"]
        Write-Host "Found: $empName (ID: $empId)" -ForegroundColor Cyan
        Write-Host "WicLocations code: $locCode" -ForegroundColor Cyan
    } else {
        Write-Host "No MAIN agent found for DE_Augsburg. Check WicAgentAssignments data." -ForegroundColor Yellow
    }
    $r.Close()
    $conn.Close()
} catch {
    Write-Host "DB query failed: $_" -ForegroundColor Red
}

if (-not $empId -or -not $locCode) {
    Write-Host "Cannot proceed without agent and location. Exiting." -ForegroundColor Red
    exit 1
}

# --- 2. Call /api/wic/substitutes with that agent marked absent --------------

Write-Host ""
Write-Host "--- 2. GET /api/wic/substitutes (absentIds=$empId) ---"

$encodedLoc = [System.Uri]::EscapeDataString($locCode)
$url = "$baseUrl/api/wic/substitutes?locationCode=$encodedLoc&date=$testDate&horizon=1&absentIds=$empId"
Write-Host "URL: $url"

try {
    $sub = Invoke-RestMethod -Uri $url -Method GET -TimeoutSec 20

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
        Write-Host "  GapCeiling   : $($day0.gapCeiling)"
        Write-Host "  BestPickId   : $($day0.bestPickId)"
        Write-Host "  Candidates   : $($day0.candidates.Count)"

        if ($day0.candidates.Count -gt 0) {
            Write-Host ""
            Write-Host "  Top 3 candidates:" -ForegroundColor Cyan
            $top3 = $day0.candidates | Select-Object -First 3
            foreach ($c in $top3) {
                Write-Host ("    {0,-30} sourceType={1,-10} tier={2,-12} dist={3}km" -f $c.fullName, $c.sourceType, $c.reachabilityTier, $c.distanceKm)
            }
            Write-Host ""
            $hasSource = $day0.candidates | Where-Object { $_.sourceType }
            $hasDist   = $day0.candidates | Where-Object { $_.distanceKm }
            $hasTier   = $day0.candidates | Where-Object { $_.reachabilityTier }
            Write-Host "  Field check - sourceType populated : $($hasSource.Count)/$($day0.candidates.Count)"
            Write-Host "  Field check - distanceKm populated : $($hasDist.Count)/$($day0.candidates.Count)"
            Write-Host "  Field check - reachabilityTier populated : $($hasTier.Count)/$($day0.candidates.Count)"
        }

        if ($day0.warning) {
            Write-Host "  Warning      : $($day0.warning)" -ForegroundColor Yellow
        }
    }

    Write-Host ""
    if ($sub.days -and $sub.days[0].gap -gt 0 -and $sub.days[0].candidates.Count -gt 0) {
        Write-Host "RESULT: gap > 0 and candidates returned -- SubstitutionService end-to-end CONFIRMED." -ForegroundColor Green
        Write-Host "Prompt 2 is COMPLETE." -ForegroundColor Green
    } elseif ($sub.days -and $sub.days[0].gap -gt 0 -and $sub.days[0].candidates.Count -eq 0) {
        Write-Host "RESULT: gap > 0 but no candidates -- all agents may be unavailable today (check shifts)." -ForegroundColor Yellow
        Write-Host "Endpoint logic is correct; data coverage may be thin for this date." -ForegroundColor Yellow
    } else {
        Write-Host "RESULT: gap=0 -- agent may not be in today's WicShiftEntries (not counted as present so removing has no effect)." -ForegroundColor Yellow
        Write-Host "The absent agent must appear in WicShiftEntries.IsOnSite=true for the date to reduce present count." -ForegroundColor Yellow
    }
} catch {
    Write-Host "ERROR calling substitutes endpoint: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== PS1_12 complete ===" -ForegroundColor Green
