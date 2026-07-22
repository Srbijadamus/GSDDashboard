#Requires -Version 7.0
# Build frontend → Backend\wwwroot, then rebuild backend Release exe.
# Run this after any code change. The scheduled task (GSDDashboard-Backend)
# runs the Release exe, so skipping this step means the old binary stays live.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── 1. Frontend ──────────────────────────────────────────────────────────────
Write-Host "`nBuilding frontend..." -ForegroundColor Cyan
Set-Location "C:\GSDDashboard\Frontend"
npm run build
if ($LASTEXITCODE -ne 0) { Write-Error "Frontend build failed"; exit 1 }

Write-Host "Copying to Backend\wwwroot..." -ForegroundColor Cyan
$dest = "C:\GSDDashboard\Backend\wwwroot"
Remove-Item "$dest\*" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "C:\GSDDashboard\Frontend\dist\*" -Destination $dest -Recurse -Force

# ── 2. Backend Release exe ────────────────────────────────────────────────────
Write-Host "`nBuilding backend (Release)..." -ForegroundColor Cyan
Set-Location "C:\GSDDashboard\Backend"
dotnet build -c Release --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Backend build failed"; exit 1 }

Write-Host "`nDone. Restart the GSDDashboard-Backend task to pick up the new binary:" -ForegroundColor Green
Write-Host "  schtasks /end /tn GSDDashboard-Backend ; schtasks /run /tn GSDDashboard-Backend"
Write-Host ""
Write-Host "  GSDDashboard: https://d2jn94qg-5000.euw.devtunnels.ms" -ForegroundColor Yellow
