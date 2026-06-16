# PS1_19_FinalBuildVerify.ps1
# Build frontend + backend, deploy, run comprehensive verification.
# No here-strings. No Unicode.

$frontendDir = "C:\GSDDashboard\Frontend"
$backendDir  = "C:\GSDDashboard\Backend"
$wwwroot     = "C:\GSDDashboard\Backend\wwwroot"
$distDir     = "C:\GSDDashboard\Frontend\dist"
$baseUrl     = "http://localhost:5000"
$tunnelUrl   = "https://8nh5k5g1-5000.euw.devtunnels.ms"

Write-Host ""
Write-Host "=== PS1_19: Final Build + Verification ===" -ForegroundColor Yellow

# ── 1. npm run build ─────────────────────────────────────────────────────────

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

# ── 2. Verify dist ───────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 2. Verify dist ---" -ForegroundColor Cyan
if (-not (Test-Path $distDir)) {
    Write-Host "ERROR: dist not found at $distDir" -ForegroundColor Red; exit 1
}
$distFiles = Get-ChildItem $distDir -Recurse | Measure-Object
Write-Host "dist: $($distFiles.Count) files" -ForegroundColor Green

$indexHtml = Join-Path $distDir "index.html"
if (Test-Path $indexHtml) {
    Write-Host "index.html: present" -ForegroundColor Green
} else {
    Write-Host "WARNING: index.html missing from dist" -ForegroundColor Yellow
}

# ── 3. Stop backend ──────────────────────────────────────────────────────────

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

# ── 4. Copy dist to wwwroot ──────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 4. Copy dist to $wwwroot ---" -ForegroundColor Cyan
if (Test-Path $wwwroot) { Remove-Item -Path $wwwroot -Recurse -Force }
Copy-Item -Path $distDir -Destination $wwwroot -Recurse
$wwwCount = (Get-ChildItem $wwwroot -Recurse | Measure-Object).Count
Write-Host "wwwroot: $wwwCount files" -ForegroundColor Green

# ── 5. Build backend ─────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 5. dotnet build -c Release ---" -ForegroundColor Cyan
Push-Location $backendDir
dotnet build --configuration Release 2>&1 | Write-Host
$backendBuild = $LASTEXITCODE
Pop-Location

if ($backendBuild -ne 0) {
    Write-Host "BACKEND BUILD FAILED -- stopping." -ForegroundColor Red; exit 1
}
Write-Host "Backend build: SUCCESS" -ForegroundColor Green

# ── 6. Start server ──────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 6. Start server ---" -ForegroundColor Cyan
$serverProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $backendDir, "--configuration", "Release", "--no-build" `
    -WorkingDirectory $backendDir `
    -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 9
Write-Host "Server PID: $($serverProc.Id)" -ForegroundColor Cyan

# ── 7. API endpoint verification ─────────────────────────────────────────────

Write-Host ""
Write-Host "--- 7. API endpoint verification ---" -ForegroundColor Cyan

$apiTests = @(
    @{ url = "$baseUrl/health";                                                label = "health" },
    @{ url = "$baseUrl/api/wic/locations";                                     label = "wic/locations" },
    @{ url = "$baseUrl/api/wic/forecast?horizon=7";                            label = "wic/forecast?horizon=7" },
    @{ url = "$baseUrl/api/wic/briefing";                                      label = "wic/briefing" },
    @{ url = "$baseUrl/api/wic/substitutes?locationCode=DE~86150~Augsburg~Schaezlerstr.%203&date=2026-06-15"; label = "wic/substitutes (Augsburg)" }
)

$allOk = $true
foreach ($test in $apiTests) {
    try {
        $resp = Invoke-WebRequest -Uri $test.url -Method GET -TimeoutSec 15 -UseBasicParsing
        $status = $resp.StatusCode
        $ok = $status -ge 200 -and $status -lt 300
        if ($ok) { Write-Host ("  {0,-40} HTTP {1}" -f $test.label, $status) -ForegroundColor Green }
        else      { Write-Host ("  {0,-40} HTTP {1}" -f $test.label, $status) -ForegroundColor Yellow; $allOk = $false }
    } catch {
        $statusCode = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { $null }
        Write-Host ("  {0,-40} {1}" -f $test.label, $(if ($statusCode) { "HTTP $statusCode" } else { "ERROR: $_" })) -ForegroundColor Red
        $allOk = $false
    }
}

# ── 8. Page verification ─────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- 8. Page (SPA) verification ---" -ForegroundColor Cyan
$pageTests = @(
    @{ url = "$baseUrl/"; label = "/ (overview)" },
    @{ url = "$baseUrl/wic-attendance"; label = "/wic-attendance" }
)
foreach ($test in $pageTests) {
    try {
        $resp = Invoke-WebRequest -Uri $test.url -Method GET -TimeoutSec 10 -UseBasicParsing
        $ct = $resp.Headers["Content-Type"] -join ""
        $isHtml = $ct -like "*html*"
        $status = $resp.StatusCode
        if ($status -ge 200 -and $status -lt 300 -and $isHtml) {
            Write-Host ("  {0,-30} HTTP {1} content-type=text/html" -f $test.label, $status) -ForegroundColor Green
        } else {
            Write-Host ("  {0,-30} HTTP {1} content-type={2}" -f $test.label, $status, $ct) -ForegroundColor Yellow
            $allOk = $false
        }
    } catch {
        Write-Host ("  {0,-30} ERROR: {1}" -f $test.label, $_) -ForegroundColor Red; $allOk = $false
    }
}

# ── 9. Theme check ───────────────────────────────────────────────────────────
# next-themes sets class="dark"|"light" on <html> at runtime via JS hydration,
# not in the static HTML served by the server. Absence of class= in raw HTML
# is expected and is NOT a bug.

Write-Host ""
Write-Host "--- 9. Theme check (ThemeProvider sets class at runtime) ---" -ForegroundColor Cyan
$indexContent = (Invoke-WebRequest -Uri "$baseUrl/" -UseBasicParsing).Content
if ($indexContent -match "<script" -and $indexContent -match "<!DOCTYPE") {
    Write-Host "  SPA shell present (next-themes applies class=dark/light at runtime via JS -- OK)" -ForegroundColor Green
} else {
    Write-Host "  WARNING: unexpected HTML structure" -ForegroundColor Yellow
}

# ── 10. i18n check ───────────────────────────────────────────────────────────
# Matches only the VALUE portion of JSON lines (after ': "') to avoid
# false positives from key names (e.g. "cmdPlaceholder") or substrings
# of German words (e.g. "nan" inside "Listenansicht").

Write-Host ""
Write-Host "--- 10. i18n source check ---" -ForegroundColor Cyan
$i18nPath = "C:\GSDDashboard\Frontend\src\i18n"
$found = $false

# Patterns matched case-insensitively in the VALUE part of JSON
$valuePatterns = @("TODO","FIXME","lorem","undefined","missing")
foreach ($pat in $valuePatterns) {
    $hits = Get-ChildItem $i18nPath -Recurse -Filter "*.json" |
            Select-String -Pattern (': ".*' + [regex]::Escape($pat)) -CaseSensitive:$false
    if ($hits) {
        Write-Host "  WARNING: '$pat' found in i18n values:" -ForegroundColor Yellow
        $hits | ForEach-Object { Write-Host "    $($_.Filename):$($_.LineNumber) $($_.Line.Trim())" }
        $found = $true
    }
}

# "placeholder" -- only flag when the VALUE literally equals "placeholder"
$hits = Get-ChildItem $i18nPath -Recurse -Filter "*.json" |
        Select-String -Pattern ': "placeholder"' -CaseSensitive:$false
if ($hits) {
    Write-Host "  WARNING: 'placeholder' found as a literal value in i18n:" -ForegroundColor Yellow
    $hits | ForEach-Object { Write-Host "    $($_.Filename):$($_.LineNumber) $($_.Line.Trim())" }
    $found = $true
}

# "NaN" -- case-sensitive so it does not match "nan" inside German words
$hits = Get-ChildItem $i18nPath -Recurse -Filter "*.json" |
        Select-String -Pattern '"NaN"' -CaseSensitive
if ($hits) {
    Write-Host "  WARNING: literal NaN found in i18n:" -ForegroundColor Yellow
    $hits | ForEach-Object { Write-Host "    $($_.Filename):$($_.LineNumber) $($_.Line.Trim())" }
    $found = $true
}

if (-not $found) { Write-Host "  No bad patterns found in i18n" -ForegroundColor Green }

# ── Summary ──────────────────────────────────────────────────────────────────

Write-Host ""
if ($allOk) {
    Write-Host "All checks PASSED" -ForegroundColor Green
} else {
    Write-Host "Some checks FAILED -- see output above" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Tunnel URLs ===" -ForegroundColor Cyan
Write-Host "  $tunnelUrl/"               -ForegroundColor DarkCyan
Write-Host "  $tunnelUrl/wic-attendance" -ForegroundColor DarkCyan
Write-Host "  $tunnelUrl/api/wic/briefing" -ForegroundColor DarkCyan
Write-Host ""
Write-Host "=== PS1_19 complete ===" -ForegroundColor Green
