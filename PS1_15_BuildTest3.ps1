# PS1_15_BuildTest3.ps1
# Build, start, and smoke-test all Prompt 3 endpoints.
# No here-strings. No Unicode. Run from powershell.exe 5.1 or 7.

$backendDir = "C:\GSDDashboard\Backend"
$dllPath    = "C:\GSDDashboard\Backend\bin\Release\net8.0\GSDDashboard.API.dll"
$baseUrl    = "http://localhost:5000"
$testDate   = "2026-06-15"

# ── 1. Stop any running instance ──────────────────────────────────────────────

Write-Host ""
Write-Host "--- 1. Stop existing GSDDashboard.API process ---"
$proc = Get-Process -Name "GSDDashboard.API" -ErrorAction SilentlyContinue
if ($proc) {
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Seconds 2
    Write-Host "Stopped PID $($proc.Id)." -ForegroundColor Green
} else {
    Write-Host "No running process." -ForegroundColor DarkGreen
}

# ── 2. Build ──────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 2. dotnet build -c Release ---"
Push-Location $backendDir
dotnet build --configuration Release 2>&1 | Write-Host
$buildExit = $LASTEXITCODE
Pop-Location

if ($buildExit -ne 0) {
    Write-Host "BUILD FAILED -- stopping." -ForegroundColor Red
    exit 1
}
Write-Host "BUILD SUCCEEDED" -ForegroundColor Green

# ── 3. DLL timestamp ──────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 3. DLL LastWriteTime ---"
if (Test-Path $dllPath) {
    $dll = Get-Item $dllPath
    Write-Host ("  {0}   {1}" -f $dll.Name, $dll.LastWriteTime) -ForegroundColor Cyan
}

# ── 4. Start server ───────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 4. Start server ---"
$serverProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $backendDir, "--configuration", "Release", "--no-build" `
    -WorkingDirectory $backendDir `
    -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 8
Write-Host "Server PID: $($serverProc.Id)" -ForegroundColor Cyan

# ── 5. Reachability sanity ────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 5. GET /api/wic/reachability/sanity ---"
try {
    $sanity = Invoke-RestMethod -Uri "$baseUrl/api/wic/reachability/sanity" -Method GET -TimeoutSec 15
    Write-Host ($sanity | ConvertTo-Json -Depth 3)
    if ($sanity.passed -eq $true) {
        Write-Host "Reachability: PASSED ($($sanity.distanceKm) km)" -ForegroundColor Green
    } else {
        Write-Host "Reachability: FAILED" -ForegroundColor Red
    }
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
}

# ── 6. GET /api/wic/forecast ──────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 6. GET /api/wic/forecast?horizon=3 ---"
try {
    $fc = Invoke-RestMethod -Uri "$baseUrl/api/wic/forecast?horizon=3" -Method GET -TimeoutSec 20
    Write-Host "generatedAt      : $($fc.generatedAt)"
    Write-Host "horizon          : $($fc.horizon)"
    Write-Host "locationCount    : $($fc.locationCount)"
    Write-Host "totalAtRiskDays  : $($fc.totalAtRiskDays)"
    if ($fc.locations -and $fc.locations.Count -gt 0) {
        $first = $fc.locations[0]
        Write-Host "First location   : $($first.displayName) -- atRiskDays=$($first.atRiskDays)"
        if ($first.days -and $first.days.Count -gt 0) {
            $d0 = $first.days[0]
            Write-Host "  Day 0 ($($d0.date)) : isOpen=$($d0.isOpen) status=$($d0.status) eff=$($d0.effectiveCoverage) min=$($d0.minRequired) isAtRisk=$($d0.isAtRisk)"
        }
    }
    Write-Host "forecast endpoint: OK" -ForegroundColor Green
} catch {
    Write-Host "ERROR calling /api/wic/forecast: $_" -ForegroundColor Red
}

# ── 7. GET /api/wic/forecast with locationCode ────────────────────────────────

Write-Host ""
Write-Host "--- 7. GET /api/wic/forecast?horizon=7&locationCode=... (Augsburg) ---"
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$augCode = $null
try {
    $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT TOP 1 wl.LocationCode FROM WicLocations wl INNER JOIN WicAgentAssignments waa ON wl.LocationCode = waa.LocationCode OR wl.LocationCodeLegacy = waa.LocationCode WHERE waa.LocationCode = 'DE_Augsburg' AND wl.IsActive = 1"
    $cmd.CommandTimeout = 10
    $augCode = [string]$cmd.ExecuteScalar()
    $conn.Close()
} catch {
    Write-Host "DB lookup failed: $_" -ForegroundColor Yellow
}

if ($augCode) {
    $encodedAug = [System.Uri]::EscapeDataString($augCode)
    try {
        $fcAug = Invoke-RestMethod -Uri "$baseUrl/api/wic/forecast?horizon=7&locationCode=$encodedAug" -Method GET -TimeoutSec 15
        Write-Host "locationCount : $($fcAug.locationCount) (expected 1)"
        if ($fcAug.locations -and $fcAug.locations.Count -gt 0) {
            $loc = $fcAug.locations[0]
            Write-Host "Location      : $($loc.displayName), atRiskDays=$($loc.atRiskDays)"
            Write-Host "Days          : $($loc.days.Count)"
        }
        Write-Host "forecast single-location: OK" -ForegroundColor Green
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
    }
} else {
    Write-Host "Skipping -- could not resolve Augsburg LocationCode" -ForegroundColor Yellow
}

# ── 8. GET /api/wic/briefing ──────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 8. GET /api/wic/briefing?date=$testDate ---"
try {
    $br = Invoke-RestMethod -Uri "$baseUrl/api/wic/briefing?date=$testDate" -Method GET -TimeoutSec 20
    Write-Host "date             : $($br.date)"
    Write-Host "totalAbsences    : $($br.totalAbsences)"
    Write-Host "totalGaps        : $($br.totalGaps)"
    Write-Host "nextAtRiskDays   : $($br.nextAtRiskDays.Count)"
    if ($br.gaps -and $br.gaps.Count -gt 0) {
        $g0 = $br.gaps[0]
        Write-Host "First gap        : $($g0.displayName) status=$($g0.status) bestSub=$($g0.bestSubstituteName)"
    }
    Write-Host "briefing endpoint: OK" -ForegroundColor Green
} catch {
    Write-Host "ERROR calling /api/wic/briefing: $_" -ForegroundColor Red
}

# ── 9. GET /api/wic/whatif ────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 9. GET /api/wic/whatif (find first MAIN agent) ---"
$empId = $null
$locCode2 = $null
try {
    $conn2 = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn2.Open()
    $cmd2 = $conn2.CreateCommand()
    $cmd2.CommandText = "SELECT TOP 1 e.EmployeeId, wl.LocationCode FROM Employees e INNER JOIN WicAgentAssignments waa ON e.FullName = waa.EmployeeName INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode OR wl.LocationCodeLegacy = waa.LocationCode WHERE waa.AssignmentType = 'MAIN' AND waa.IsActive = 1 AND e.IsActive = 1 ORDER BY wl.LocationCode"
    $cmd2.CommandTimeout = 10
    $r = $cmd2.ExecuteReader()
    if ($r.Read()) {
        $empId   = [string]$r["EmployeeId"]
        $locCode2 = [string]$r["LocationCode"]
    }
    $r.Close()
    $conn2.Close()
} catch {
    Write-Host "DB lookup failed: $_" -ForegroundColor Yellow
}

if ($empId) {
    Write-Host "Testing whatif for empId=$empId (WIC: $locCode2)"
    try {
        $wi = Invoke-RestMethod -Uri "$baseUrl/api/wic/whatif?absentEmployeeId=$empId&date=$testDate&horizon=3" -Method GET -TimeoutSec 20
        Write-Host "employeeId        : $($wi.employeeId)"
        Write-Host "fullName          : $($wi.fullName)"
        Write-Host "affectedLocations : $($wi.affectedLocations.Count)"
        if ($wi.affectedLocations -and $wi.affectedLocations.Count -gt 0) {
            $al = $wi.affectedLocations[0]
            Write-Host "First location    : $($al.displayName) assignmentType=$($al.assignmentType)"
            if ($al.substitution -and $al.substitution.days -and $al.substitution.days.Count -gt 0) {
                $day0 = $al.substitution.days[0]
                Write-Host "  Day 0: status=$($day0.currentStatus) present=$($day0.present) gap=$($day0.gap) candidates=$($day0.candidates.Count)"
            }
        }
        Write-Host "whatif endpoint: OK" -ForegroundColor Green
    } catch {
        Write-Host "ERROR calling /api/wic/whatif: $_" -ForegroundColor Red
    }
} else {
    Write-Host "Skipping -- no MAIN agent found" -ForegroundColor Yellow
}

# ── 10. Existing substitutes endpoint regression ──────────────────────────────

Write-Host ""
Write-Host "--- 10. GET /api/wic/substitutes regression (Augsburg with absent agent) ---"
if ($augCode -and $empId) {
    $encodedAug2 = [System.Uri]::EscapeDataString($augCode)
    try {
        $sub = Invoke-RestMethod -Uri "$baseUrl/api/wic/substitutes?locationCode=$encodedAug2&date=$testDate&horizon=1&absentIds=$empId" -Method GET -TimeoutSec 20
        Write-Host "locationCode  : $($sub.locationCode)"
        Write-Host "displayName   : $($sub.displayName)"
        if ($sub.days -and $sub.days.Count -gt 0) {
            $day0 = $sub.days[0]
            Write-Host "  status=$($day0.currentStatus) present=$($day0.present) gap=$($day0.gap) candidates=$($day0.candidates.Count)"
            if ($day0.candidates.Count -gt 0) {
                $top = $day0.candidates[0]
                Write-Host "  Top: $($top.fullName) [$($top.sourceType)] loadScore=$($top.loadScore)"
            }
        }
        Write-Host "substitutes regression: OK" -ForegroundColor Green
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
    }
} else {
    Write-Host "Skipping -- missing location or employee" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== PS1_15 complete ===" -ForegroundColor Green
Write-Host "Tunnel URL for external tests:" -ForegroundColor Cyan
Write-Host "  https://n8jlr9dr-5000.euw.devtunnels.ms/api/wic/forecast?horizon=3" -ForegroundColor Cyan
Write-Host "  https://n8jlr9dr-5000.euw.devtunnels.ms/api/wic/briefing" -ForegroundColor Cyan
Write-Host "  https://n8jlr9dr-5000.euw.devtunnels.ms/api/wic/whatif?absentEmployeeId=EMPID&date=2026-06-15" -ForegroundColor Cyan
