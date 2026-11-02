$dt       = 'C:\Users\S69307\AppData\Local\Microsoft\WinGet\Packages\Microsoft.devtunnel_Microsoft.Winget.Source_8wekyb3d8bbwe\devtunnel.exe'
$LOG      = 'C:\HealthCheck\watchdog_shiftkiosk_tunnel.log'
$HEALTH   = 'https://ssr7tm2l-8000.euw.devtunnels.ms/health'
$LOCAL    = 'http://localhost:8000/health'

function WLog($m) {
    $line = "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $m"
    Add-Content $LOG $line -Encoding UTF8
    $lines = Get-Content $LOG -ErrorAction SilentlyContinue
    if ($lines.Count -gt 300) { $lines | Select-Object -Last 300 | Set-Content $LOG -Encoding UTF8 }
}

WLog 'WATCHDOG START (startup delay 45s)'
Start-Sleep -Seconds 45

while ($true) {
    WLog 'TUNNEL START shift-kiosk'
    $proc = Start-Process -PassThru -WindowStyle Hidden -FilePath $dt -ArgumentList 'host shift-kiosk'
    if ($null -eq $proc) {
        WLog 'TUNNEL PROC NULL; retry in 10s'
        Start-Sleep -Seconds 10
        continue
    }
    WLog "TUNNEL PID=$($proc.Id)"
    $failCount = 0
    while ($true) {
        Start-Sleep -Seconds 30
        $proc.Refresh()
        if ($proc.HasExited) {
            WLog "TUNNEL EXITED code=$($proc.ExitCode); restart in 10s"
            $failCount = 0
            Start-Sleep -Seconds 10
            break
        }
        $tunelFail = $false
        try {
            $r = Invoke-WebRequest -Uri $HEALTH -TimeoutSec 10 -UseBasicParsing -MaximumRedirection 0 -ErrorAction Stop
            if ($r.StatusCode -ne 200) { $tunelFail = $true }
        } catch { $tunelFail = $true }

        $localOk = $false
        try {
            $rl = Invoke-WebRequest -Uri $LOCAL -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
            if ($rl.StatusCode -eq 200) { $localOk = $true }
        } catch { $localOk = $false }

        if (-not $tunelFail) {
            $failCount = 0
            WLog "HEALTH OK PID=$($proc.Id)"
        } elseif ($tunelFail -and $localOk) {
            $failCount++
            WLog "ZOMBIE CANDIDATE tunelFail=1 localOk=1 count=$failCount PID=$($proc.Id)"
            if ($failCount -ge 2) {
                WLog "ZOMBIE 2xFAIL PID=$($proc.Id); killing"
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                $failCount = 0
                Start-Sleep -Seconds 10
                break
            }
        } else {
            $failCount = 0
            WLog "SERVER DOWN tunelFail=1 localOk=0; not killing host PID=$($proc.Id)"
        }
    }
}
