# PS1_18_BuildAndDeploy.ps1
# Build frontend (vite writes directly into Backend/wwwroot, see
# Frontend/vite.config.ts outDir), restart backend, smoke-test.
# No here-strings. No Unicode.

$frontendDir = "C:\GSDDashboard\Frontend"
$backendDir  = "C:\GSDDashboard\Backend"
$wwwroot     = "C:\GSDDashboard\Backend\wwwroot"
$baseUrl     = "http://localhost:5000"

Write-Host ""
Write-Host "=== PS1_18: Frontend Build + Deploy ===" -ForegroundColor Yellow

# ── 1. npm run build ───────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 1. npm run build ---" -ForegroundColor Cyan
Push-Location $frontendDir
npm run build 2>&1 | Write-Host
$buildExit = $LASTEXITCODE
Pop-Location

if ($buildExit -ne 0) {
    Write-Host "FRONTEND BUILD FAILED (exit $buildExit) -- stopping." -ForegroundColor Red
    exit 1
}
Write-Host "Frontend build: SUCCESS" -ForegroundColor Green

# ── 2. Verify wwwroot (vite already wrote the build here directly) ───────────

Write-Host ""
Write-Host "--- 2. Verify wwwroot output ---" -ForegroundColor Cyan
$indexHtml = Join-Path $wwwroot "index.html"
if (-not (Test-Path $indexHtml)) {
    Write-Host "ERROR: index.html not found in $wwwroot" -ForegroundColor Red
    exit 1
}
$wwwFiles = Get-ChildItem $wwwroot -Recurse | Measure-Object
Write-Host "wwwroot: $($wwwFiles.Count) files" -ForegroundColor Green

# CARTO key sanity check (defense in depth -- vite.config.ts already aborts
# the build itself if VITE_CARTO_KEY is missing, see 2026-10 watermark incident).
$bundle = Get-ChildItem (Join-Path $wwwroot "assets\index-*.js") -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $bundle) {
    Write-Host "ERROR: no built JS bundle found in $wwwroot\assets" -ForegroundColor Red
    exit 1
}
$bundleText = Get-Content $bundle.FullName -Raw
if ($bundleText -notmatch 'cartocdn\.com/[^"'']*key=[^"''&]+') {
    Write-Host "ERROR: built bundle '$($bundle.Name)' has no non-empty CARTO key -- stopping before touching the running backend." -ForegroundColor Red
    exit 1
}
Write-Host "CARTO key check: OK ($($bundle.Name))" -ForegroundColor Green

# ── 3. Stop backend ────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 3. Stop existing GSDDashboard.API process ---" -ForegroundColor Cyan
$proc = Get-Process -Name "GSDDashboard.API" -ErrorAction SilentlyContinue
if ($proc) {
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Seconds 2
    Write-Host "Stopped PID $($proc.Id)." -ForegroundColor Green
} else {
    Write-Host "No running process." -ForegroundColor DarkGreen
}

# ── 4. (removed) ───────────────────────────────────────────────────────────────
# No copy step needed: vite.config.ts's build.outDir points directly at
# Backend\wwwroot (emptyOutDir: true), so step 1's `npm run build` already put
# the verified bundle there. A previous version of this script deleted
# Backend\wwwroot and copied from Frontend\dist, which no longer exists since
# the outDir change -- that would have wiped the live site's static files on
# every run. Do not reintroduce a dist-based copy here.

# ── 5. Build backend (Release) ─────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 5. dotnet build backend -c Release ---" -ForegroundColor Cyan
Push-Location $backendDir
dotnet build --configuration Release 2>&1 | Write-Host
$backendBuild = $LASTEXITCODE
Pop-Location

if ($backendBuild -ne 0) {
    Write-Host "BACKEND BUILD FAILED -- stopping." -ForegroundColor Red
    exit 1
}
Write-Host "Backend build: SUCCESS" -ForegroundColor Green

# ── 6. Start server ────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 6. Start server ---" -ForegroundColor Cyan
$serverProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $backendDir, "--configuration", "Release", "--no-build" `
    -WorkingDirectory $backendDir `
    -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 8
Write-Host "Server PID: $($serverProc.Id)" -ForegroundColor Cyan

# ── 7. Smoke-test endpoints ────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 7. Smoke-test new frontend endpoints ---" -ForegroundColor Cyan

$tests = @(
    @{ url = "$baseUrl/api/wic/forecast?horizon=3"; label = "forecast" },
    @{ url = "$baseUrl/api/wic/cards?date=2026-06-15"; label = "cards" },
    @{ url = "$baseUrl/api/wic/briefing"; label = "briefing" },
    @{ url = "$baseUrl/health"; label = "health" },
    @{ url = "$baseUrl/"; label = "index.html (SPA)" }
)

$allOk = $true
foreach ($test in $tests) {
    try {
        $resp = Invoke-WebRequest -Uri $test.url -Method GET -TimeoutSec 15 -UseBasicParsing
        $status = $resp.StatusCode
        $ok = $status -ge 200 -and $status -lt 300
        if ($ok) {
            Write-Host ("  {0,-20} HTTP {1}" -f $test.label, $status) -ForegroundColor Green
        } else {
            Write-Host ("  {0,-20} HTTP {1}" -f $test.label, $status) -ForegroundColor Yellow
            $allOk = $false
        }
    } catch {
        $statusCode = $null
        if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
        Write-Host ("  {0,-20} {1}" -f $test.label, $(if ($statusCode) { "HTTP $statusCode" } else { "ERROR: $_" })) -ForegroundColor Red
        $allOk = $false
    }
}

Write-Host ""
if ($allOk) {
    Write-Host "All smoke-tests PASSED" -ForegroundColor Green
} else {
    Write-Host "Some smoke-tests FAILED -- check output above" -ForegroundColor Red
}

# ── 8. Tunnel URLs ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Tunnel URLs ===" -ForegroundColor Cyan
Write-Host "  https://n8jlr9dr-5000.euw.devtunnels.ms/" -ForegroundColor DarkCyan
Write-Host "  https://n8jlr9dr-5000.euw.devtunnels.ms/wic-attendance" -ForegroundColor DarkCyan
Write-Host "  https://n8jlr9dr-5000.euw.devtunnels.ms/api/wic/forecast?horizon=3" -ForegroundColor DarkCyan

Write-Host ""
Write-Host "=== PS1_18 complete ===" -ForegroundColor Green
