# Smoke-Pulse.ps1 — read-only smoke test posle restarta.
# Svaka provera: PASS/FAIL + kratak razlog. Rezultat -> C:\PulseOps\smoke\smoke_<ts>.txt
# Nista ne restartuje, ne zaustavlja, ne menja.

$ErrorActionPreference = 'SilentlyContinue'
$boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
$script:failCount = 0
$script:results = [System.Collections.Generic.List[string]]::new()

function Add-Result([string]$id, [bool]$pass, [string]$reason) {
    $status = if ($pass) { 'PASS' } else { 'FAIL' }
    if (-not $pass) { $script:failCount++ }
    $script:results.Add("$status  $id) $reason")
}

function Get-LogTs([string]$line) {
    try {
        if ($line -match '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})') {
            return [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)
        }
    } catch {}
    return [datetime]::MinValue
}

# a) boot time + uptime
$uptime = (Get-Date) - $boot
$uptimeStr = "{0}d {1}h {2}m" -f $uptime.Days, $uptime.Hours, $uptime.Minutes
Add-Result 'a' $true "poslednje pokretanje $($boot.ToString('yyyy-MM-dd HH:mm:ss')); uptime $uptimeStr"

# b) OK linija u notify_restart.log posle boot-a (vremena u UTC)
$bootUtc = $boot.ToUniversalTime()
$okAfterBoot = $false
if (Test-Path 'C:\PulseOps\notify_restart.log') {
    foreach ($l in (Get-Content 'C:\PulseOps\notify_restart.log' -Encoding UTF8)) {
        if ($l -match 'OK$' -and (Get-LogTs $l) -ge $bootUtc) { $okAfterBoot = $true; break }
    }
}
Add-Result 'b' $okAfterBoot "notify_restart.log $(if($okAfterBoot){'ima OK liniju posle boot-a'}else{'NEMA OK liniju posle boot-a'})"

# c) taskovi
$tasks = 'GSDDashboard-Backend','ShiftKioskServer','GSDDashboard-Tunnel-v2','ShiftKioskTunnel','PuzzledPlaneTunnel','LaptopTracker-App','ServiceHealthCheck'
foreach ($tname in $tasks) {
    $t = Get-ScheduledTask -TaskName $tname -ErrorAction SilentlyContinue
    if (-not $t) {
        Add-Result 'c' $false "$tname : nije citljiv / ne postoji (access denied?)"
        continue
    }
    $expected = if ($tname -in @('ServiceHealthCheck')) { @('Ready','Running') } else { @('Running') }
    $ok = ($expected -contains $t.State)
    Add-Result 'c' $ok "$tname = $($t.State) (ocekivano $($expected -join '/'))"
}

# d1) GET / = 200
$dStatus = 0; $idx = $null
try { $r = Invoke-WebRequest 'http://localhost:5000/' -UseBasicParsing -TimeoutSec 10; $dStatus = $r.StatusCode; if ($r.Content -match 'index-[A-Za-z0-9_-]+\.js') { $idx = $Matches[0] } } catch { $dStatus = 0 }
Add-Result 'd1' ($dStatus -eq 200) "GET http://localhost:5000/ = $dStatus, index=$idx"

# d2) index u OBA static roota
$d2ok = $false
if ($idx) {
    $f1 = Get-ChildItem 'C:\GSDDashboard\Backend\wwwroot' -Recurse -Filter $idx -ErrorAction SilentlyContinue | Select-Object -First 1
    $f2 = Get-ChildItem 'C:\GSDDashboard\Backend\bin\Release\net8.0\wwwroot' -Recurse -Filter $idx -ErrorAction SilentlyContinue | Select-Object -First 1
    $d2ok = [bool]($f1 -and $f2)
}
Add-Result 'd2' $d2ok "index $idx u OBA static roota $(if($d2ok){'da'}else{'NE'})"

# e) kiosk health + attendance
$h = 0; $att = 0
try { $h = (Invoke-WebRequest 'http://localhost:8000/health' -UseBasicParsing -TimeoutSec 10).StatusCode } catch {}
try { $att = (Invoke-WebRequest 'http://localhost:8000/api/attendance' -UseBasicParsing -TimeoutSec 10).StatusCode } catch {}
Add-Result 'e' ($h -eq 200 -and $att -eq 200) "kiosk health=$h, api/attendance=$att"

# f) LaptopTracker :5016 slusa
$conn5016 = Get-NetTCPConnection -LocalPort 5016 -State Listen -ErrorAction SilentlyContinue
$fOk = [bool]$conn5016
$fPid = if ($conn5016) { $conn5016[0].OwningProcess } else { '-' }
Add-Result 'f' $fOk "LaptopTracker :5016 $(if($fOk){"slusa (PID $fPid)"}else{'ne slusa'})"

# g) devtunnel procesi
$dt = @(Get-CimInstance Win32_Process -Filter "Name='devtunnel.exe'" -ErrorAction SilentlyContinue)
$hosts = @()
foreach ($p in $dt) { if ($p.CommandLine -match 'host\s+(\S+)') { $hosts += $Matches[1] } }
$expectedT = 'gsd-dashboard-v2','shift-kiosk','puzzled-plane-1cfdm0z'
$missing = @()
foreach ($e in $expectedT) { if (-not ($hosts | Where-Object { $_ -like "*$e*" })) { $missing += $e } }
Add-Result 'g' ($missing.Count -eq 0) "devtunnel hostovi: $($hosts -join ', ')$(if($missing.Count){' | NEDOSTAJE: ' + ($missing -join ',')})"

# h) MSSQL$SQLEXPRESS
$svc = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue
$svcStatus = if ($svc) { $svc.Status } else { 'nema servisa' }
Add-Result 'h' ($svc -and $svc.Status -eq 'Running') "MSSQL`$SQLEXPRESS = $svcStatus"

# i) 0 .NET Runtime 1026 + 0 Application Error za GSDDashboard/python od boot-a
$net1026 = @(Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='.NET Runtime'; Id=1026; StartTime=$boot} -ErrorAction SilentlyContinue)
$appErr = @(Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Application Error'; Id=1000; StartTime=$boot} -ErrorAction SilentlyContinue | Where-Object { $_.Message -match 'GSDDashboard\.API\.exe|python\.exe' })
Add-Result 'i' ($net1026.Count -eq 0 -and $appErr.Count -eq 0) ".NET Runtime 1026=$($net1026.Count), AppError(GSDDashboard/python)=$($appErr.Count)"

# j) sentinel
$sent = Test-Path 'C:\HealthCheck\DEPLOY_IN_PROGRESS'
Add-Result 'j' (-not $sent) "sentinel $(if($sent){'POSTOJI (pauza deploya!)'}else{'ne postoji'})"

# k) poslednja linija health.log
$lastHealth = Get-Content 'C:\HealthCheck\health.log' -Tail 1 -Encoding UTF8 -ErrorAction SilentlyContinue
Add-Result 'k' ($lastHealth -ne $null) "health.log: $lastHealth"

# summary + write
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$dir = 'C:\PulseOps\smoke'
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$outFile = Join-Path $dir "smoke_$ts.txt"
$overall = if ($script:failCount -eq 0) { 'PASS' } else { 'FAIL' }
$script:results.Add('')
$script:results.Add("UKUPNO: $overall (FAIL-ova: $script:failCount)")
$content = $script:results -join "`r`n"
$content | Set-Content -Path $outFile -Encoding UTF8
Write-Output $content
Write-Output "Rezultat: $outFile"
