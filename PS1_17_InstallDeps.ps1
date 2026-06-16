# PS1_17_InstallDeps.ps1
# Install missing frontend dependencies for Prompt 4.
# Installs: react-leaflet, leaflet, @types/leaflet
# Does NOT upgrade Tailwind CSS (stays on 3.4.19).
# No here-strings. No Unicode.

$frontendDir = "C:\GSDDashboard\Frontend"

Write-Host ""
Write-Host "=== PS1_17: Install frontend dependencies ===" -ForegroundColor Yellow
Write-Host "Working dir: $frontendDir"

if (-not (Test-Path $frontendDir)) {
    Write-Host "ERROR: Frontend directory not found: $frontendDir" -ForegroundColor Red
    exit 1
}

Push-Location $frontendDir

# Show current node/npm version
Write-Host ""
Write-Host "--- node/npm versions ---" -ForegroundColor Cyan
node --version
npm --version

# Check current tailwind version (must stay on 3.x)
Write-Host ""
Write-Host "--- Current tailwindcss version ---" -ForegroundColor Cyan
$twVer = npm list tailwindcss --depth=0 2>&1 | Select-String "tailwindcss"
Write-Host $twVer -ForegroundColor DarkCyan

# Install react-leaflet, leaflet, @types/leaflet
Write-Host ""
Write-Host "--- Installing react-leaflet leaflet @types/leaflet ---" -ForegroundColor Cyan
npm install react-leaflet leaflet
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    Write-Host "ERROR: npm install failed (exit $exitCode)" -ForegroundColor Red
    Pop-Location
    exit 1
}

npm install --save-dev @types/leaflet
$exitCode2 = $LASTEXITCODE
if ($exitCode2 -ne 0) {
    Write-Host "WARNING: @types/leaflet install failed (exit $exitCode2) -- continuing" -ForegroundColor Yellow
}

# Verify installed
Write-Host ""
Write-Host "--- Verify installed packages ---" -ForegroundColor Cyan
npm list react-leaflet leaflet --depth=0 2>&1 | Write-Host
npm list @types/leaflet --depth=0 2>&1 | Write-Host

# Confirm tailwind did NOT get upgraded
Write-Host ""
Write-Host "--- Tailwind version after install (must still be 3.x) ---" -ForegroundColor Cyan
$twVerAfter = npm list tailwindcss --depth=0 2>&1 | Select-String "tailwindcss"
Write-Host $twVerAfter -ForegroundColor DarkCyan

Pop-Location

Write-Host ""
Write-Host "=== PS1_17 complete ===" -ForegroundColor Green
