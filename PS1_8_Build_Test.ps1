# PS1_8_Build_Test.ps1
# Build, start, and smoke-test the GSDDashboard API.
# No here-strings. No Unicode. Run from powershell.exe 5.1.

$backendDir = "C:\GSDDashboard\Backend"
$dllPath    = "C:\GSDDashboard\Backend\bin\Release\net8.0\GSDDashboard.API.dll"
$cs         = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$baseUrl    = "http://localhost:5000"
$testDate   = "2026-06-15"

# --- 1. Stop any running instance --------------------------------------------

Write-Host ""
Write-Host "--- 1. Stop existing GSDDashboard.API process ---"
$proc = Get-Process -Name "GSDDashboard.API" -ErrorAction SilentlyContinue
if ($proc) {
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Seconds 2
    Write-Host "Stopped PID $($proc.Id)." -ForegroundColor Green
} else {
    Write-Host "No running process found." -ForegroundColor DarkGreen
}

# --- 2. Build ----------------------------------------------------------------

Write-Host ""
Write-Host "--- 2. dotnet build -c Release ---"
Push-Location $backendDir
dotnet build --configuration Release 2>&1 | Write-Host
$buildExit = $LASTEXITCODE
Pop-Location

if ($buildExit -ne 0) {
    Write-Host "BUILD FAILED - stopping." -ForegroundColor Red
    exit 1
}
Write-Host "BUILD SUCCEEDED" -ForegroundColor Green

# --- 3. DLL timestamp --------------------------------------------------------

Write-Host ""
Write-Host "--- 3. DLL LastWriteTime ---"
if (Test-Path $dllPath) {
    $dll = Get-Item $dllPath
    Write-Host ("  {0}   {1}" -f $dll.Name, $dll.LastWriteTime) -ForegroundColor Cyan
} else {
    Write-Host "  DLL not found at $dllPath" -ForegroundColor Yellow
}

# --- 4. Start server ---------------------------------------------------------

Write-Host ""
Write-Host "--- 4. Start server (Release, no-build) ---"
$serverProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $backendDir, "--configuration", "Release", "--no-build" `
    -WorkingDirectory $backendDir `
    -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 8
Write-Host "Server PID: $($serverProc.Id)" -ForegroundColor Cyan

# --- 5. GET /api/wic/reachability/sanity -------------------------------------

Write-Host ""
Write-Host "--- 5. GET /api/wic/reachability/sanity ---"
try {
    $sanity = Invoke-RestMethod -Uri "$baseUrl/api/wic/reachability/sanity" -Method GET -TimeoutSec 15
    Write-Host ($sanity | ConvertTo-Json -Depth 5)
    if ($sanity.passed -eq $true) {
        Write-Host "Reachability sanity: PASSED" -ForegroundColor Green
    } else {
        Write-Host "Reachability sanity: FAILED - check coordinates" -ForegroundColor Red
    }
} catch {
    Write-Host "ERROR calling /api/wic/reachability/sanity : $_" -ForegroundColor Red
}

# --- 6. Find a MAIN agent (legacy-code-aware join) ---------------------------

Write-Host ""
Write-Host "--- 6. Find MAIN agent via WicAgentAssignments ---"

$locCode   = $null
$agentName = $null

$sqlFind = "SELECT TOP 1 wl.LocationCode AS NewCode, waa.EmployeeName FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode OR wl.LocationCodeLegacy = waa.LocationCode WHERE waa.AssignmentType = 'MAIN' AND waa.IsActive = 1 AND wl.IsActive = 1 ORDER BY wl.LocationCode"

try {
    $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sqlFind
    $cmd.CommandTimeout = 15
    $r = $cmd.ExecuteReader()
    if ($r.Read()) {
        $locCode   = [string]$r["NewCode"]
        $agentName = [string]$r["EmployeeName"]
        Write-Host "Found: $agentName at $locCode" -ForegroundColor Cyan
    } else {
        Write-Host "No MAIN agent found - falling back to first active WicLocation" -ForegroundColor Yellow
    }
    $r.Close()
    if (-not $locCode) {
        $cmdFb = $conn.CreateCommand()
        $cmdFb.CommandText = "SELECT TOP 1 LocationCode FROM WicLocations WHERE IsActive = 1 ORDER BY LocationCode"
        $cmdFb.CommandTimeout = 15
        $locCode = [string]$cmdFb.ExecuteScalar()
        Write-Host "Fallback location: $locCode" -ForegroundColor Yellow
    }
    $conn.Close()
} catch {
    Write-Host "DB query failed: $_" -ForegroundColor Red
}

# --- 7. GET /api/wic/substitutes - full JSON response ------------------------

Write-Host ""
Write-Host "--- 7. GET /api/wic/substitutes ---"

if ($locCode) {
    $url = "$baseUrl/api/wic/substitutes?locationCode=$locCode&date=$testDate&horizon=1"
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
        Write-Host "  Horizon      : $($sub.horizon)"
        if ($sub.days -and $sub.days.Count -gt 0) {
            $day0 = $sub.days[0]
            Write-Host "  Day 0 ($($day0.date)) : status=$($day0.currentStatus)  present=$($day0.present)  gap=$($day0.gap)"
            Write-Host "  BestPickId   : $($day0.bestPickId)"
            Write-Host "  Candidates   : $($day0.candidates.Count)"
            if ($day0.candidates.Count -gt 0) {
                $top = $day0.candidates[0]
                Write-Host "  Top candidate: $($top.fullName) [$($top.sourceType)] tier=$($top.reachabilityTier) dist=$($top.distanceKm)km"
            }
        }
        Write-Host "Substitutes endpoint: OK" -ForegroundColor Green
    } catch {
        Write-Host "ERROR calling substitutes endpoint : $_" -ForegroundColor Red
    }
} else {
    Write-Host "Skipping - no location code available." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== PS1_8 complete ===" -ForegroundColor Green
