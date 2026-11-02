param()
#
# Two-tier health check + ACL monitor.
# Tier 1: local HTTP /health -> 3 consecutive failures -> restart app task
# Tier 2: tunnel URL          -> 3 consecutive failures -> restart tunnel task
# ACL:    devtunnel access list every 30 min -> LOUD WARNING if tenant entry missing
# Rate:   max 3 restarts/hour per service to prevent restart storms
#
# State persisted between runs in .hc_state.json (failure counters + hourly restart counts).
# Restart actions logged separately in restart_actions.log.
#

#region === CONFIG ===
$HC             = 'C:\HealthCheck'
$LOG            = "$HC\health.log"
$RESTART_LOG    = "$HC\restart_actions.log"
$STATE_FILE     = "$HC\.hc_state.json"
$MAX_LOG        = 500
$FAIL_THRESH    = 3          # consecutive failures before acting
$MAX_RST_HR     = 3          # max restarts per service per hour
$LOCAL_TMO      = 3000       # local HTTP timeout ms
$TUNNEL_TMO     = 9000       # tunnel HTTP timeout ms
$ACL_INTERVAL   = 30         # minutes between ACL checks
$EXPECTED_TID   = 'b914a242-e718-443b-a47c-6b4c649d8c0a'
$DT             = 'C:\Users\S69307\AppData\Local\Microsoft\WinGet\Packages\Microsoft.devtunnel_Microsoft.Winget.Source_8wekyb3d8bbwe\devtunnel.exe'
#endregion

#region === SERVICES ===
$SVC = @(
    [PSCustomObject]@{
        Name       = 'ShiftKiosk'
        Port       = 8000
        LocalUrl   = 'http://localhost:8000/health'
        TunnelUrl  = 'https://ssr7tm2l-8000.euw.devtunnels.ms/health'
        TunnelId   = 'shift-kiosk'
        AppTask    = 'ShiftKioskServer'
        TunnelTask = 'ShiftKioskTunnel'
    }
    [PSCustomObject]@{
        Name       = 'GSDDashboard'
        Port       = 5000
        LocalUrl   = 'http://localhost:5000/health'
        TunnelUrl  = 'https://d2jn94qg-5000.euw.devtunnels.ms/health'
        TunnelId   = 'gsd-dashboard-v2'
        AppTask    = 'GSDDashboard-Backend'
        TunnelTask = 'GSDDashboard-Tunnel-v2'
    }
    [PSCustomObject]@{
        Name       = 'LaptopTracker'
        Port       = 5016
        LocalUrl   = 'http://localhost:5016/health'
        TunnelUrl  = 'https://puzzled-plane-1cfdm0z-5016.euw.devtunnels.ms/health'
        TunnelId   = 'puzzled-plane-1cfdm0z'
        AppTask    = 'LaptopTracker-App'
        TunnelTask = 'PuzzledPlaneTunnel'
    }
)
#endregion

#region === LOGGING ===
function HcLog {
    param([string]$m, [string]$f = $LOG)
    Add-Content $f -Value "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $m" -Encoding UTF8
}

function TrimLog {
    param([string]$f = $LOG)
    if (-not (Test-Path $f)) { return }
    $l = Get-Content $f
    if ($l.Count -gt $MAX_LOG) { $l | Select-Object -Last $MAX_LOG | Set-Content $f -Encoding UTF8 }
}

function Show-Toast {
    param([string]$title, [string]$body)
    try {
        [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime] | Out-Null
        $appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
        $xml   = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent(
                     [Windows.UI.Notifications.ToastTemplateType]::ToastText02)
        $xml.GetElementsByTagName('text')[0].AppendChild($xml.CreateTextNode($title)) | Out-Null
        $xml.GetElementsByTagName('text')[1].AppendChild($xml.CreateTextNode($body))  | Out-Null
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId).Show(
            [Windows.UI.Notifications.ToastNotification]::new($xml))
    } catch { }
}
#endregion

#region === STATE (PS 5.1 compatible — ConvertFrom-Json returns PSCustomObject) ===
function HourKey { (Get-Date -f 'yyyyMMddHH') }

function Load-State {
    $def = @{ _aclTs = '' }
    foreach ($s in $SVC) {
        $def[$s.Name] = @{ lf=0; tf=0; arh=0; arHr=''; trh=0; trHr='' }
    }
    if (-not (Test-Path $STATE_FILE)) { return $def }
    try {
        $j = Get-Content $STATE_FILE -Raw -Encoding UTF8 | ConvertFrom-Json
        $r = @{}
        foreach ($p in $j.PSObject.Properties) {
            if ($p.Name -eq '_aclTs') {
                # PS5.1 ConvertFrom-Json auto-coerces ISO date strings to DateTime.
                $v = $p.Value
                $r['_aclTs'] = if ($v -is [DateTime]) { $v.ToString('o') } else { [string]$v }
                continue
            }
            $v = $p.Value
            $r[$p.Name] = @{
                lf   = [int]$v.lf;   tf   = [int]$v.tf
                arh  = [int]$v.arh;  arHr = [string]$v.arHr
                trh  = [int]$v.trh;  trHr = [string]$v.trHr
            }
        }
        foreach ($k in $def.Keys) { if (-not $r.ContainsKey($k)) { $r[$k] = $def[$k] } }
        return $r
    } catch { return $def }
}

function Save-State { param([hashtable]$st)
    try { $st | ConvertTo-Json -Depth 4 | Set-Content $STATE_FILE -Encoding UTF8 } catch { }
}

# Returns how many restarts already used this hour; 0 if hour rolled over.
function RestartsThisHour { param([int]$cnt, [string]$hr)
    if ($hr -ne (HourKey)) { return 0 }
    return $cnt
}

# Returns updated count+hour after recording one more restart.
function RecordRestart { param([int]$cnt, [string]$hr)
    $hk = HourKey
    if ($hr -ne $hk) { return @{ n=1; h=$hk } }
    return @{ n=($cnt+1); h=$hk }
}
#endregion

#region === HEALTH PROBES ===
function Test-Http { param([string]$url, [int]$tmo)
    # Returns $true if server responds with any HTTP status < 500 (including redirects).
    try {
        $req = [System.Net.WebRequest]::Create($url)
        $req.Timeout = $tmo
        $req.AllowAutoRedirect = $true
        try {
            $resp = $req.GetResponse()
            $code = [int]$resp.StatusCode
            $resp.Close()
            return ($code -lt 500)
        } catch [System.Net.WebException] {
            if ($_.Exception.Response) {
                return ([int]$_.Exception.Response.StatusCode -lt 500)
            }
            return $false
        }
    } catch { return $false }
}

function Get-DevtunnelAuth {
    try {
        $out = & $DT user show 2>&1 | Out-String
        if ($out -match 'Not logged in|not authenticated') { return 'EXPIRED' }
        return 'OK'
    } catch { return 'UNKNOWN' }
}

function Check-Acl { param([string]$tunnelId)
    try {
        $out = & $DT access list $tunnelId 2>&1 | Out-String
        return @{
            hasConnect = ($out -match ([regex]::Escape('+AAD Tenant [connect]')))
            hasExpTid  = ($out -match [regex]::Escape($EXPECTED_TID))
        }
    } catch { return @{ hasConnect=$false; hasExpTid=$false } }
}
#endregion

#region === RESTART ACTIONS ===
function Restart-AppTask { param([string]$task, [int]$port, [string]$why)
    try {
        HcLog "ACTION restart-app $task ($why)" $RESTART_LOG
        Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        # Kill any orphan process still holding the port so new instance can bind.
        Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
            ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 1
        Start-ScheduledTask -TaskName $task
        HcLog "ACTION started $task" $RESTART_LOG
        return $true
    } catch {
        HcLog "ACTION FAILED $task : $_" $RESTART_LOG
        return $false
    }
}

function Restart-TunnelTask { param([string]$task, [string]$tunnelId, [string]$why)
    try {
        HcLog "ACTION restart-tunnel $task ($why)" $RESTART_LOG
        Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        # Kill orphan devtunnel processes whose parent PS is now dead.
        $dtProcs = Get-CimInstance Win32_Process -Filter "Name='devtunnel.exe'" -ErrorAction SilentlyContinue
        foreach ($dt in $dtProcs) {
            $parentAlive = Get-Process -Id $dt.ParentProcessId -ErrorAction SilentlyContinue
            if (-not $parentAlive) {
                Stop-Process -Id $dt.ProcessId -Force -ErrorAction SilentlyContinue
            }
        }
        Start-Sleep -Seconds 1
        Start-ScheduledTask -TaskName $task
        HcLog "ACTION started $task" $RESTART_LOG
        return $true
    } catch {
        HcLog "ACTION FAILED $task : $_" $RESTART_LOG
        return $false
    }
}
#endregion

# ==============================================================
# MAIN
# ==============================================================
TrimLog; TrimLog $RESTART_LOG

$state    = Load-State
$now      = Get-Date
$failures = @()
$acts     = @()

# --- devtunnel auth ---
$auth = Get-DevtunnelAuth
if ($auth -ne 'OK') {
    $failures += "devtunnel auth $auth"
    HcLog "FAIL devtunnel auth $auth - re-login required"
}

# --- ACL check every $ACL_INTERVAL minutes ---
$lastAcl  = $state['_aclTs']
try   { $lastAclDt = [DateTime]::Parse([string]$lastAcl) } catch { $lastAclDt = [DateTime]::MinValue }
$doAcl    = ($lastAclDt -eq [DateTime]::MinValue) -or
            ((New-TimeSpan -Start $lastAclDt -End $now).TotalMinutes -ge $ACL_INTERVAL)
if ($doAcl) {
    foreach ($s in $SVC) {
        $acl = Check-Acl $s.TunnelId
        if (-not $acl.hasConnect) {
            $msg = "ACL CRITICAL $($s.TunnelId): '+AAD Tenant [connect]' MISSING — COLLEAGUES LOCKED OUT"
            HcLog $msg; $failures += "ACL-$($s.Name)-missing"
            Show-Toast 'ACL Critical' $msg
        } elseif (-not $acl.hasExpTid) {
            $msg = "ACL WARNING $($s.TunnelId): unexpected tenant (expected $EXPECTED_TID)"
            HcLog $msg; $failures += "ACL-$($s.Name)-wrong-tenant"
        } else {
            HcLog "ACL OK $($s.TunnelId)"
        }
    }
    $state['_aclTs'] = $now.ToString('o')
}

# --- Per-service two-tier check ---
foreach ($s in $SVC) {
    $st = $state[$s.Name]

    # ==== Tier 1: local HTTP health ====
    $localOk = Test-Http $s.LocalUrl $LOCAL_TMO
    if ($localOk) {
        if ($st.lf -gt 0) { HcLog "RECOVER $($s.Name) local :$($s.Port) back up" }
        $st.lf = 0
    } else {
        $st.lf++
        $msg = "$($s.Name) local :$($s.Port) DOWN ($($st.lf)/$FAIL_THRESH)"
        HcLog "FAIL $msg"; $failures += $msg

        if ($st.lf -ge $FAIL_THRESH) {
            $used = RestartsThisHour $st.arh $st.arHr
            if ($used -lt $MAX_RST_HR) {
                if (Restart-AppTask $s.AppTask $s.Port "local DOWN $($st.lf)x") {
                    $r = RecordRestart $used $st.arHr
                    $st.arh = $r.n; $st.arHr = $r.h; $st.lf = 0
                    $acts += "$($s.AppTask) restarted (local DOWN)"
                }
            } else {
                HcLog "GUARD $($s.Name) app restarts=$used/$MAX_RST_HR this hour — holding off"
                $failures += "$($s.Name) app-restart-storm-guard"
            }
        }
    }

    # ==== Tier 2: tunnel health (only if local is OK) ====
    if ($localOk) {
        $tunOk = Test-Http $s.TunnelUrl $TUNNEL_TMO
        if ($tunOk) {
            if ($st.tf -gt 0) { HcLog "RECOVER $($s.Name) tunnel back up" }
            $st.tf = 0
        } else {
            $st.tf++
            $msg = "$($s.Name) tunnel DOWN local-OK ($($st.tf)/$FAIL_THRESH)"
            HcLog "FAIL $msg"; $failures += $msg

            if ($st.tf -ge $FAIL_THRESH) {
                $used = RestartsThisHour $st.trh $st.trHr
                if ($used -lt $MAX_RST_HR) {
                    if (Restart-TunnelTask $s.TunnelTask $s.TunnelId "tunnel DOWN $($st.tf)x") {
                        $r = RecordRestart $used $st.trHr
                        $st.trh = $r.n; $st.trHr = $r.h; $st.tf = 0
                        $acts += "$($s.TunnelTask) restarted (tunnel DOWN)"
                    }
                } else {
                    HcLog "GUARD $($s.Name) tunnel restarts=$used/$MAX_RST_HR this hour — holding off"
                    $failures += "$($s.Name) tunnel-restart-storm-guard"
                }
            }
        }
    } else {
        $st.tf = 0  # can't assess tunnel when local is down; reset so tunnel counter starts fresh after recovery
    }

    $state[$s.Name] = $st
}

# --- Summary line ---
$dtCount = @(Get-Process devtunnel -ErrorAction SilentlyContinue).Count
if ($acts.Count -gt 0) {
    $summary = "ACTED $($acts -join '; ')"
    if ($failures.Count -gt 0) { $summary += " | still-failing: $($failures -join ', ')" }
    HcLog $summary
    Show-Toast 'Service Auto-Restart' ($acts -join '; ')
} elseif ($failures.Count -eq 0) {
    HcLog "OK ports 8000/5000/5016 open, $dtCount tunnels, auth $auth"
} else {
    $summary = $failures -join '; '
    HcLog "ALERT: $summary"
    Show-Toast 'Service Health Alert' $summary
}

Save-State $state
