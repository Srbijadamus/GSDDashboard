param(
    [string]$LiveRoot   = 'C:\GSDDashboard\Backend',
    [string]$OutDir     = 'C:\GSDDashboard_secure\Tools\WicSyncFix20261015\Deploy',
    [int]$Port          = 5000
)
# Manual start of the promoted build while the deploy sentinel keeps the watchdog away.
# Exactly the production launch shape: the live Release apphost, its working directory, and
# NO PULSE_RUNTIME_CONFIG override, so the app loads the same
# C:\ProgramData\PulseBackend\backend.json the watchdog would give it.
$ErrorActionPreference = 'Stop'
$release = Join-Path $LiveRoot 'bin\Release\net8.0'
$exe = Join-Path $release 'GSDDashboard.API.exe'
if (!(Test-Path -LiteralPath $exe)) { throw "Missing $exe" }
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw "Port $Port is already in use."
}
if (!(Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$env:PULSE_RUNTIME_CONFIG = $null
$p = Start-Process -FilePath $exe -WorkingDirectory $LiveRoot -PassThru `
    -RedirectStandardOutput (Join-Path $OutDir 'app-manual.stdout.log') `
    -RedirectStandardError  (Join-Path $OutDir 'app-manual.stderr.log')
$p.Id | Set-Content -LiteralPath (Join-Path $OutDir 'app-manual.pid') -Encoding UTF8
Write-Output "manual app PID $($p.Id)"

$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    if ($p.HasExited) { throw 'manual app exited before readiness' }
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health" -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
}
if (!$ready) { throw 'manual app readiness timed out' }
Write-Output "health 200 after $($i + 1)s"


