# SmokeAfterLogon.ps1 — pri prvoj prijavi posle restarta (uptime < 2h) pokrece Smoke-Pulse.ps1
# i otvara najnoviji rezultat u Notepad-u. Ako je uptime veci, ne radi nista.
$boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
if (((Get-Date) - $boot).TotalHours -lt 2) {
    & 'C:\PulseOps\Smoke-Pulse.ps1'
    $f = Get-ChildItem 'C:\PulseOps\smoke\smoke_*.txt' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($f) { Start-Process notepad $f.FullName }
}
