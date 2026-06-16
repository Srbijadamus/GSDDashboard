# PS1_1_Build.ps1 — Kill existing process, build, launch, verify endpoint.
# Run in PowerShell 7.6 from any directory.
Set-StrictMode -Off

Write-Host "=== Step 1: Kill running GSDDashboard.API ===" -ForegroundColor Yellow
$proc = Get-Process GSDDashboard.API -ErrorAction SilentlyContinue
if ($proc) {
    $proc | Stop-Process -Force
    Write-Host "Killed PID $($proc.Id)"
    Start-Sleep -Seconds 3
} else {
    Write-Host "Process not running."
}

Write-Host "`n=== Step 2: dotnet build ===" -ForegroundColor Yellow
Push-Location "C:\GSDDashboard\Backend"
dotnet build -c Debug 2>&1
$buildOk = $LASTEXITCODE -eq 0
Pop-Location

if (-not $buildOk) {
    Write-Host "`nBUILD FAILED — fix errors above before continuing." -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Step 3: DLL timestamp ===" -ForegroundColor Yellow
$dll = Get-Item "C:\GSDDashboard\Backend\bin\Debug\net8.0\GSDDashboard.API.dll"
Write-Host "LastWriteTime: $($dll.LastWriteTime)"

Write-Host "`n=== Step 4: Launch server ===" -ForegroundColor Yellow
Start-Process `
    -FilePath "C:\GSDDashboard\Backend\bin\Debug\net8.0\GSDDashboard.API.exe" `
    -WorkingDirectory "C:\GSDDashboard\Backend" `
    -WindowStyle Minimized
Start-Sleep -Seconds 5

Write-Host "`n=== Step 5: Test /api/wic/locations (first 2 items) ===" -ForegroundColor Yellow
try {
    $locs = Invoke-RestMethod "http://localhost:5000/api/wic/locations"
    Write-Host "Total locations returned: $($locs.Count)"
    Write-Host "--- First 2 items (openingSchedule + fullAddress) ---"
    $locs[0..1] | ConvertTo-Json -Depth 4
} catch {
    Write-Host "ERROR calling endpoint: $_" -ForegroundColor Red
}

Write-Host "`n=== Step 6: Test Augsburg split block ===" -ForegroundColor Yellow
try {
    $locs = Invoke-RestMethod "http://localhost:5000/api/wic/locations"
    $aug = $locs | Where-Object { $_.city -like "*Augsburg*" -or $_.displayName -like "*Augsburg*" }
    if ($aug) {
        $aug | ConvertTo-Json -Depth 4
    } else {
        Write-Host "Augsburg not found in locations — check LocationCode / City value." -ForegroundColor Yellow
    }
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
}

Write-Host "`n=== Build and verify complete ===" -ForegroundColor Green
