# GSD Dashboard - Diagnose Backend
Write-Host "=== GSD Dashboard Diagnose ===" -ForegroundColor Cyan

# 1. Port 5000
Write-Host "`n[1] Port 5000:" -ForegroundColor Yellow
$conn = Get-NetTCPConnection -LocalPort 5000 -ErrorAction SilentlyContinue
if ($conn) {
    $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
    Write-Host "  AKTIVAN - PID $($conn.OwningProcess) ($($proc.Name))" -ForegroundColor Green
} else {
    Write-Host "  NIJE AKTIVAN - backend ne slusa na 5000" -ForegroundColor Red
}

# 2. dotnet build
Write-Host "`n[2] Build:" -ForegroundColor Yellow
$build = & dotnet build "C:\GSDDashboard\Backend" --no-restore -v q 2>&1
$errors = $build | Where-Object { $_ -match "error" }
if ($errors) {
    Write-Host "  BUILD GRESKE:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
} else {
    Write-Host "  Build OK" -ForegroundColor Green
}

# 3. Endpoint /api/bo-list
Write-Host "`n[3] Endpoint /api/bo-list:" -ForegroundColor Yellow
try {
    $r = Invoke-WebRequest "http://localhost:5000/api/bo-list?date=2026-07-15" -UseBasicParsing -TimeoutSec 5
    Write-Host "  OK - Status $($r.StatusCode)" -ForegroundColor Green
    Write-Host "  Body: $($r.Content)" -ForegroundColor Gray
} catch {
    Write-Host "  GRESKA: $($_.Exception.Message)" -ForegroundColor Red
}

# 4. Swagger
Write-Host "`n[4] Swagger /swagger/v1/swagger.json:" -ForegroundColor Yellow
try {
    $sw = Invoke-RestMethod "http://localhost:5000/swagger/v1/swagger.json" -TimeoutSec 5
    $boRoutes = $sw.paths.PSObject.Properties.Name | Where-Object { $_ -like "*bo-list*" }
    if ($boRoutes) {
        Write-Host "  bo-list rute registrovane:" -ForegroundColor Green
        $boRoutes | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
    } else {
        Write-Host "  bo-list NIJE u swagger rutama!" -ForegroundColor Red
        Write-Host "  Sve rute: $($sw.paths.PSObject.Properties.Name -join ', ')" -ForegroundColor Gray
    }
} catch {
    Write-Host "  Swagger nedostupan: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n=== Kraj dijagnoze ===" -ForegroundColor Cyan
