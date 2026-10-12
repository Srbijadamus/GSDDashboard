#Requires -Version 7.0
# Build frontend → Backend\wwwroot, then rebuild backend Release exe.
# Run this after any code change. The scheduled task (GSDDashboard-Backend)
# runs the Release exe, so skipping this step means the old binary stays live.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── 1. Frontend ──────────────────────────────────────────────────────────────
# `npm run build` (see Frontend/package.json) already runs `vite build` with
# outDir set directly to Backend\wwwroot (emptyOutDir: true) and then copies
# the result into Backend\bin\Release\net8.0\wwwroot. There is no Frontend\dist
# output to copy from — do NOT add a Remove-Item/Copy-Item step here. An old
# version of this script deleted Backend\wwwroot and tried to copy from a
# Frontend\dist that no longer exists, which would have wiped the live site's
# static files on every run. vite.config.ts also hard-fails the build itself
# if VITE_CARTO_KEY is missing, so a failed/keyless build never reaches wwwroot.
Write-Host "`nBuilding frontend..." -ForegroundColor Cyan
Set-Location "C:\GSDDashboard\Frontend"
npm run build
if ($LASTEXITCODE -ne 0) { Write-Error "Frontend build failed"; exit 1 }

# ── 1b. Post-build sanity check (defense in depth) ───────────────────────────
# Confirm the freshly built bundle actually embeds a non-empty CARTO key
# before treating the build as deployable.
$dest = "C:\GSDDashboard\Backend\wwwroot"
$bundle = Get-ChildItem "$dest\assets\index-*.js" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $bundle) {
    Write-Error "No built JS bundle found in $dest\assets — aborting before any further step."; exit 1
}
$bundleText = Get-Content $bundle.FullName -Raw
if ($bundleText -notmatch 'cartocdn\.com/[^"'']*key=[^"''&]+') {
    Write-Error "Built bundle '$($bundle.Name)' has no non-empty CARTO key — aborting. Check Frontend\.env."
    exit 1
}
Write-Host "CARTO key check: OK ($($bundle.Name))" -ForegroundColor Green

# ── 2. Backend Release exe ────────────────────────────────────────────────────
Write-Host "`nBuilding backend (Release)..." -ForegroundColor Cyan
Set-Location "C:\GSDDashboard\Backend"
dotnet build -c Release --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Backend build failed"; exit 1 }

Write-Host "`nDone. Restart the GSDDashboard-Backend task to pick up the new binary:" -ForegroundColor Green
Write-Host "  schtasks /end /tn GSDDashboard-Backend ; schtasks /run /tn GSDDashboard-Backend"
Write-Host ""
Write-Host "  GSDDashboard: https://d2jn94qg-5000.euw.devtunnels.ms" -ForegroundColor Yellow
