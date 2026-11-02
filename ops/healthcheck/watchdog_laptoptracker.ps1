$LOG = 'C:\HealthCheck\watchdog_laptoptracker.log'
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
    Push-Location 'C:\LaptopTracker\publish'
    & 'C:\Program Files\dotnet\dotnet.exe' LaptopTracker.dll --urls http://0.0.0.0:5016
    Pop-Location
    WLog "APP EXIT code=$LASTEXITCODE; restart in 10s"
    Start-Sleep -Seconds 10
}
