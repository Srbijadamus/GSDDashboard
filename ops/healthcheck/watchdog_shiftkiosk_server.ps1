$LOG = 'C:\HealthCheck\watchdog_shiftkiosk_server.log'
function WLog($m) {
    $line = "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $m"
    Add-Content $LOG $line -Encoding UTF8
    $lines = Get-Content $LOG -ErrorAction SilentlyContinue
    if ($lines.Count -gt 300) { $lines | Select-Object -Last 300 | Set-Content $LOG -Encoding UTF8 }
}

WLog 'WATCHDOG START (startup delay 30s)'
Start-Sleep -Seconds 30

while ($true) {
    WLog 'APP START'
    & 'C:\Users\S69307\AppData\Local\Programs\Python\Python312-32\python.exe' 'C:\ShiftKiosk\server\server.py'
    WLog "APP EXIT code=$LASTEXITCODE; restart in 10s"
    Start-Sleep -Seconds 10
}
