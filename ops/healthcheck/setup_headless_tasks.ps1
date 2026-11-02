#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Rung 1: Convert all service/tunnel tasks to At-Startup, Password logon.
    Run this once. Then reboot WITHOUT logging in and check the three tunnel URLs
    from an external device to confirm unattended operation works.

.NOTES
    Your password is stored in the Task Scheduler credential vault (DPAPI-encrypted,
    same protection as Windows Credential Manager). It is NOT written to disk.

    If devtunnel cannot access its auth token in headless mode after the reboot test,
    see README.md for Rung 2 (NSSM) and Rung 3 (devtunnel token) instructions.
#>

$ErrorActionPreference = 'Stop'
$PS  = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
$HC  = 'C:\HealthCheck'
$dom = $env:USERDOMAIN

Write-Host ''
Write-Host '=== Headless Services Setup - Rung 1 ==='
Write-Host "Domain: $dom"
Write-Host ''
Write-Host 'Your Windows password will be stored in the Task Scheduler vault (DPAPI-encrypted).'
Write-Host 'This allows tasks to run after a reboot without requiring your login.'
Write-Host ''

$cred = Get-Credential -UserName "$dom\S69307" `
    -Message "Enter the Windows password for $dom\S69307"
$user = $cred.UserName
$pass = $cred.GetNetworkCredential().Password

if ([string]::IsNullOrEmpty($pass)) {
    Write-Host 'ERROR: Password cannot be empty. Exiting.'
    exit 1
}

Write-Host ''
Write-Host '--- Configuring service tasks ---'

# Task definitions: Name, script filename, startup delay in seconds
# Apps get 30s, tunnels get 45s (network needs to be up; apps start in parallel)
$serviceTasks = @(
    [PSCustomObject]@{ Name='ShiftKioskServer';       Script='watchdog_shiftkiosk_server.ps1';   ExtraArgs='' }
    [PSCustomObject]@{ Name='ShiftKioskTunnel';       Script='watchdog_shiftkiosk_tunnel.ps1';   ExtraArgs='' }
    [PSCustomObject]@{ Name='GSDDashboard-Backend';   Script='watchdog_gsd_backend.ps1';         ExtraArgs='' }
    [PSCustomObject]@{ Name='GSDDashboard-Tunnel-v2'; Script='watchdog_gsd_tunnel.ps1';          ExtraArgs='' }
    [PSCustomObject]@{ Name='LaptopTracker-App';      Script='watchdog_laptoptracker.ps1';       ExtraArgs='' }
    [PSCustomObject]@{ Name='PuzzledPlaneTunnel';     Script='watchdog_puzzledplane_tunnel.ps1'; ExtraArgs='' }
)

$allOk = $true
foreach ($t in $serviceTasks) {
    Write-Host "  $($t.Name) ... " -NoNewline
    try {
        $args  = "-NonInteractive -WindowStyle Hidden -File `"$HC\$($t.Script)`""
        $action   = New-ScheduledTaskAction -Execute $PS -Argument $args
        $trigger  = New-ScheduledTaskTrigger -AtStartup
        # Delay so network is up before apps/tunnels try to connect.
        # Startup sleep is also in the watchdog scripts as a belt-and-suspenders.
        try { $trigger.Delay = 'PT20S' } catch {}

        $settings = New-ScheduledTaskSettingsSet `
            -ExecutionTimeLimit ([TimeSpan]::Zero) `
            -StartWhenAvailable `
            -MultipleInstances IgnoreNew `
            -RestartCount 99 `
            -RestartInterval (New-TimeSpan -Minutes 2)

        Register-ScheduledTask -TaskName $t.Name `
            -Action $action -Trigger $trigger -Settings $settings `
            -RunLevel Highest -User $user -Password $pass `
            -Description "Headless watchdog - auto-converted $(Get-Date -f 'yyyy-MM-dd')" `
            -Force | Out-Null

        Write-Host 'OK'
    } catch {
        Write-Host "FAILED: $_"
        $allOk = $false
    }
}

# Health check: At-Startup, repeat every 5 minutes, headless
# Uses XML because PowerShell cmdlets don't cleanly expose repetition on BootTrigger.
Write-Host "  ServiceHealthCheck (5-min repeat) ... " -NoNewline
try {
    $hcXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Checks service health every 5 minutes; logs to C:\HealthCheck\health.log</Description>
  </RegistrationInfo>
  <Triggers>
    <BootTrigger>
      <Repetition>
        <Interval>PT5M</Interval>
        <Duration>P9999D</Duration>
        <StopAtDurationEnd>false</StopAtDurationEnd>
      </Repetition>
      <Delay>PT60S</Delay>
    </BootTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>$user</UserId>
      <LogonType>Password</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>Queue</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT3M</ExecutionTimeLimit>
    <Enabled>true</Enabled>
    <StartWhenAvailable>true</StartWhenAvailable>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe</Command>
      <Arguments>-NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "C:\HealthCheck\health_check.ps1"</Arguments>
    </Exec>
  </Actions>
</Task>
"@
    Register-ScheduledTask -TaskName 'ServiceHealthCheck' `
        -Xml $hcXml -User $user -Password $pass -Force | Out-Null
    Write-Host 'OK'
} catch {
    Write-Host "FAILED: $_"
    $allOk = $false
}

# Verification pass
Write-Host ''
Write-Host '--- Verification ---'
$checkTasks = @(
    'ShiftKioskServer','ShiftKioskTunnel',
    'GSDDashboard-Backend','GSDDashboard-Tunnel-v2',
    'LaptopTracker-App','PuzzledPlaneTunnel',
    'ServiceHealthCheck'
)
foreach ($name in $checkTasks) {
    $t = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    if (-not $t) {
        Write-Host "  [MISSING] $name"
        $allOk = $false
        continue
    }
    $lt      = $t.Principal.LogonType   # Should be 'Password'
    $trigTyp = if ($t.Triggers) { $t.Triggers[0].GetType().Name -replace 'MSFT_Task|Trigger','' } else { 'none' }
    $state   = $t.State
    $ok      = ($lt -eq 'Password') -and ($state -eq 'Ready' -or $state -eq 'Running')
    $marker  = if ($ok) { 'OK' } else { 'WARN' }
    Write-Host "  [$marker] $name  logon=$lt  trigger=$trigTyp  state=$state"
    if (-not $ok) { $allOk = $false }
}

Write-Host ''
if ($allOk) {
    Write-Host 'All tasks configured. Next steps:'
    Write-Host '  1. Close all open windows.'
    Write-Host '  2. Reboot: Start -> Power -> Restart.'
    Write-Host '  3. At the login screen, do NOT log in. Wait 3 minutes.'
    Write-Host '  4. From another device, open the three tunnel URLs.'
    Write-Host '     ShiftKiosk  : https://ssr7tm2l-8000.euw.devtunnels.ms/health'
    Write-Host '     GSDDashboard: https://d2jn94qg-5000.euw.devtunnels.ms'
    Write-Host '     LaptopTracker: https://puzzled-plane-1cfdm0z-5016.euw.devtunnels.ms/ReturnDevices'
    Write-Host '  5. All three should respond. If they do: Rung 1 passed.'
    Write-Host '  6. If any are dead: log in, check C:\HealthCheck\watchdog_*.log for EXIT codes.'
    Write-Host '     Exit code 0xC000006D or similar = auth token inaccessible -> proceed to Rung 2.'
    Write-Host '     See README.md for Rung 2 (NSSM) instructions.'
} else {
    Write-Host 'WARNING: Some tasks failed verification. Review above and re-run, or check README.md.'
}
