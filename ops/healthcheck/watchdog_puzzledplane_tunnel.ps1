$dt       = 'C:\Users\S69307\AppData\Local\Microsoft\WinGet\Packages\Microsoft.devtunnel_Microsoft.Winget.Source_8wekyb3d8bbwe\devtunnel.exe'
$LOG      = 'C:\HealthCheck\watchdog_puzzledplane_tunnel.log'
$HEALTH   = 'https://puzzled-plane-1cfdm0z-5016.euw.devtunnels.ms/ReturnDevices'
$LOCAL    = 'http://localhost:5016/ReturnDevices'
$TUNNEL   = 'puzzled-plane-1cfdm0z.euw'

function WLog($m) {
    $line = "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $m"
    Add-Content $LOG $line -Encoding UTF8
    $lines = Get-Content $LOG -ErrorAction SilentlyContinue
    if ($lines.Count -gt 300) { $lines | Select-Object -Last 300 | Set-Content $LOG -Encoding UTF8 }
}

# Tunel radi ako vrati bilo koji HTTP odgovor < 500 (302 = Entra prijava, to je OK)
function Test-Url($url, $tmo) {
    try {
        $r = Invoke-WebRequest -Uri $url -TimeoutSec $tmo -UseBasicParsing -ErrorAction Stop
        return ($r.StatusCode -lt 500)
    } catch {
        $code = $null
        try { $code = $_.Exception.Response.StatusCode.value__ } catch {}
        if ($code -and $code -lt 500) { return $true }
        return $false
    }
}

WLog 'WATCHDOG START (startup delay 45s)'
Start-Sleep -Seconds 45

while ($true) {
    WLog "TUNNEL START $TUNNEL"
    $proc = Start-Process -PassThru -WindowStyle Hidden -FilePath $dt -ArgumentList "host $TUNNEL"
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
        $tunelFail = -not (Test-Url $HEALTH 10)
        $localOk   = Test-Url $LOCAL 5

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
