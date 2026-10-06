$LOG = 'C:\ProgramData\PulseBackend\watchdog.log'
function WLog($message) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message"
    Add-Content -LiteralPath $LOG -Value $line -Encoding UTF8
    $lines = Get-Content -LiteralPath $LOG
    if ($lines.Count -gt 300) {
        $lines | Select-Object -Last 300 | Set-Content -LiteralPath $LOG -Encoding UTF8
    }
}

$SENTINEL = 'C:\HealthCheck\DEPLOY_IN_PROGRESS'
$STALE_MINUTES = 30
WLog 'WATCHDOG START (startup delay 30s)'
Start-Sleep -Seconds 30
while ($true) {
    if (Test-Path -LiteralPath $SENTINEL) {
        $ageMinutes = [int]((Get-Date) - (Get-Item -LiteralPath $SENTINEL).LastWriteTime).TotalMinutes
        if ($ageMinutes -ge $STALE_MINUTES) {
            WLog "DEPLOY SENTINEL STALE (age ${ageMinutes}m > ${STALE_MINUTES}m) - removed, resuming"
            Remove-Item -LiteralPath $SENTINEL -Force
        } else {
            WLog 'DEPLOY PAUSED (sentinel present)'
            Start-Sleep -Seconds 30
            continue
        }
    }
    WLog 'APP START'
    Push-Location 'C:\GSDDashboard\Backend'
    & 'C:\GSDDashboard\Backend\bin\Release\net8.0\GSDDashboard.API.exe'
    Pop-Location
    WLog "APP EXIT code=$LASTEXITCODE; restart in 10s"
    Start-Sleep -Seconds 10
}
