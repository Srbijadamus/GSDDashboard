# Restart GSD Dashboard Backend
$port = 5000

# Find and kill process on port 5000
$pid5000 = (Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue).OwningProcess
if ($pid5000) {
    Write-Host "Stopping process PID $pid5000 on port $port..." -ForegroundColor Yellow
    Stop-Process -Id $pid5000 -Force
    Start-Sleep -Seconds 1
    Write-Host "Stopped." -ForegroundColor Green
} else {
    Write-Host "No process found on port $port." -ForegroundColor Gray
}

# Start backend
Write-Host "Starting backend..." -ForegroundColor Cyan
Set-Location "C:\GSDDashboard\Backend"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "dotnet run"
Write-Host "Backend started in new window." -ForegroundColor Green
