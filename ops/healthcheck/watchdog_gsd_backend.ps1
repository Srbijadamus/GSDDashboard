$LOG = 'C:\HealthCheck\watchdog_gsd_backend.log'
function WLog($m) {
    $line = "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $m"
    Add-Content $LOG $line -Encoding UTF8
    $lines = Get-Content $LOG -ErrorAction SilentlyContinue
    if ($lines.Count -gt 300) { $lines | Select-Object -Last 300 | Set-Content $LOG -Encoding UTF8 }
}

$SENTINEL = 'C:\HealthCheck\DEPLOY_IN_PROGRESS'
$STALE_MINUTES = 30

WLog 'WATCHDOG START (startup delay 30s)'
Start-Sleep -Seconds 30

while ($true) {
    if (Test-Path $SENTINEL) {
        $ageMin = [int]((Get-Date) - (Get-Item $SENTINEL).LastWriteTime).TotalMinutes
        if ($ageMin -ge $STALE_MINUTES) {
            WLog "DEPLOY SENTINEL STALE (age ${ageMin}m > ${STALE_MINUTES}m) - removed, resuming"
            Remove-Item $SENTINEL -Force -ErrorAction SilentlyContinue
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
