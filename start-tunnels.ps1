#Requires -Version 7.0
<#
.SYNOPSIS
    Manual recovery: restarts all three GSD apps + devtunnel hosts.
    Use this when a process dies without a reboot (e.g. terminal crash).
    On an actual reboot the Windows Scheduled Tasks handle startup automatically.

.USAGE
    pwsh -File C:\GSDDashboard\start-tunnels.ps1

Scheduled task names (what runs on boot/logon):
  GSDDashboard-Backend   → C:\GSDDashboard\Backend\bin\Release\net8.0\GSDDashboard.API.exe
  GSDDashboard-Tunnel-v2 → devtunnel host gsd-dashboard-v2
  ShiftKioskServer       → python C:\ShiftKiosk\server\server.py
  ShiftKioskTunnel       → devtunnel host shift-kiosk
  LaptopTracker-App      → dotnet LaptopTracker.dll --urls http://0.0.0.0:5016
  PuzzledPlaneTunnel     → devtunnel host puzzled-plane-1cfdm0z
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$DEVTUNNEL = 'C:\Users\S69307\AppData\Local\Microsoft\WinGet\Packages\Microsoft.devtunnel_Microsoft.Winget.Source_8wekyb3d8bbwe\devtunnel.exe'
$PYTHON    = 'C:\Users\S69307\AppData\Local\Programs\Python\Python312-32\python.exe'
$DOTNET    = 'C:\Program Files\dotnet\dotnet.exe'

function Start-Hidden {
    param([string]$Label, [string]$Exe, [string]$Args = '', [string]$WorkDir = '', [hashtable]$Env = @{})
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName        = $Exe
    $psi.Arguments       = $Args
    $psi.UseShellExecute = $false
    $psi.WindowStyle     = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $psi.CreateNoWindow  = $true
    if ($WorkDir) { $psi.WorkingDirectory = $WorkDir }
    foreach ($kv in $Env.GetEnumerator()) { $psi.EnvironmentVariables[$kv.Key] = $kv.Value }
    $proc = [System.Diagnostics.Process]::Start($psi)
    Write-Host "  [$Label] PID $($proc.Id)"
}

Write-Host "`n=== GSD Manual Recovery ===" -ForegroundColor Cyan
Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`n"

# ── Kill stale processes ──────────────────────────────────────────────────────
Write-Host "--- Stopping stale processes ---" -ForegroundColor Yellow
foreach ($port in @(5000, 8000, 5016)) {
    $lines = netstat -ano 2>$null | Select-String ":${port} " | Where-Object { $_ -match 'ABH' }
    $lines | ForEach-Object {
        if ($_ -match '\s+(\d+)\s*$') {
            Stop-Process -Id $Matches[1] -Force -ErrorAction SilentlyContinue
            Write-Host "  Killed PID $($Matches[1]) on port ${port}"
        }
    }
}
Get-Process devtunnel -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "  devtunnel.exe processes stopped"

Start-Sleep 2

# ── Start apps ────────────────────────────────────────────────────────────────
Write-Host "`n--- Starting apps ---" -ForegroundColor Yellow

Start-Hidden 'GSDDashboard API (Release)' `
    'C:\GSDDashboard\Backend\bin\Release\net8.0\GSDDashboard.API.exe' `
    '' 'C:\GSDDashboard\Backend'

Start-Hidden 'ShiftKiosk Python' `
    $PYTHON 'C:\ShiftKiosk\server\server.py' 'C:\ShiftKiosk\server'

Start-Hidden 'LaptopTracker' `
    $DOTNET 'LaptopTracker.dll --urls http://0.0.0.0:5016' 'C:\LaptopTracker\publish'

Write-Host "`n  Waiting 28s for apps (ShiftKiosk has ~11s DB timeout)..." -ForegroundColor DarkGray
Start-Sleep 28

# ── Start tunnel hosts ────────────────────────────────────────────────────────
Write-Host "`n--- Starting tunnel hosts ---" -ForegroundColor Yellow

Start-Hidden 'devtunnel: gsd-dashboard-v2'       $DEVTUNNEL 'host gsd-dashboard-v2'
Start-Hidden 'devtunnel: shift-kiosk'             $DEVTUNNEL 'host shift-kiosk'
Start-Hidden 'devtunnel: puzzled-plane-1cfdm0z'   $DEVTUNNEL 'host puzzled-plane-1cfdm0z'

Start-Sleep 8

# ── Verify ────────────────────────────────────────────────────────────────────
Write-Host "`n--- Verification ---" -ForegroundColor Yellow
$checks = @(
    @{ name = 'GSDDashboard'; url = 'http://localhost:5000/health' }
    @{ name = 'ShiftKiosk';   url = 'http://localhost:8000/health' }
    @{ name = 'LaptopTracker';url = 'http://localhost:5016/'       }
)
foreach ($check in $checks) {
    try {
        $r = Invoke-WebRequest -Uri $check.url -TimeoutSec 6 -UseBasicParsing -ErrorAction Stop
        Write-Host "  $($check.name): HTTP $($r.StatusCode) OK" -ForegroundColor Green
    } catch {
        Write-Host "  $($check.name): FAIL - $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`n=== Tunnel URLs ===" -ForegroundColor Cyan
Write-Host "  GSDDashboard  : https://d2jn94qg-5000.euw.devtunnels.ms  (anonymous)"
Write-Host "  ShiftKiosk    : https://ssr7tm2l-8000.euw.devtunnels.ms  (anonymous)"
Write-Host "  LaptopTracker : https://295qv7hp-5016.euw.devtunnels.ms  (WIC, anonymous)"
Write-Host "                  https://295qv7hp-3001.euw.devtunnels.ms  (frontend)"
Write-Host "                  https://295qv7hp-8080.euw.devtunnels.ms  (alt)"
Write-Host ""
