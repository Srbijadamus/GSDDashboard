param(
    [string]$LocalBase  = 'http://127.0.0.1:5000',
    [string]$TunnelBase = 'https://d2jn94qg-5000.euw.devtunnels.ms',
    [string]$OutFile    = 'C:\GSDDashboard_secure\Tools\SickPeriodFix20261016\Deploy\routes-manual-82.json',
    [string]$Kid        = 'S69307',
    # '82' = the documented 82-route set (27 SPA + 55 API) every deploy since 2026-10-13
    # verified. 'all' additionally probes the parameterised routes that already exist.
    # This task adds NO new route path, so 82/82 stays the documented acceptance.
    [ValidateSet('all','82')][string]$Set = '82'
)
# Login uses the DEV operator KID, never the RTM KID (S60028).
$ErrorActionPreference = 'Continue'
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }
$T   = (Get-Date).ToString('yyyy-MM-dd')
$T13 = (Get-Date).AddDays(13).ToString('yyyy-MM-dd')
$T30 = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')

$sessions = @{}
foreach ($base in @($LocalBase, $TunnelBase)) {
    $s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $st = -1; $role = ''
    try {
        $l = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType 'application/json' `
            -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $s -Headers $H -TimeoutSec 30
        $st = 200; $role = $l.role
    } catch { $role = "login-failed: $($_.Exception.Message)" }
    $sessions[$base] = @{ session = $s; login = $st; role = $role }
}

$locEnc = ''
try {
    $locs = (Invoke-WebRequest -Uri "$LocalBase/api/wic/locations" -WebSession $sessions[$LocalBase].session -Headers $H -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    $locEnc = [uri]::EscapeDataString([string]($locs | Where-Object { $_.displayName -eq 'Grafenrheinfeld' } | Select-Object -First 1).locationCode)
    if (!$locEnc) { $locEnc = [uri]::EscapeDataString([string]$locs[0].locationCode) }
} catch { $locEnc = [uri]::EscapeDataString('DE~97506~Grafenrheinfeld~Kraftwerkstraße') }

$spaRoutes = @('/', '/shifts', '/roster', '/wic-shifts', '/vwic', '/breaks', '/training', '/pipeline',
    '/rollout', '/wic-attendance', '/remote-attendance', '/wic-schedule', '/wic', '/wic-coverage',
    '/wic-al', '/attendance', '/sickleave', '/vacations', '/albalance', '/alcalendar', '/employees',
    '/assistant', '/bo-list', '/bulk-rtm', '/export', '/my', '/login')
$apiRoutes = @('/health', '/api/pipeline', '/api/rollout',
    "/api/rollout/candidates?locationCode=$locEnc&date=$T",
    "/api/rollout/standard-hours?locationCode=$locEnc&date=$T",
    '/api/wic/locations', "/api/wic?from=$T&to=$T13", "/api/wic/open?date=$T", "/api/wic/cards?date=$T",
    '/api/wic/briefing', '/api/wic/forecast?horizon=7', "/api/wic/conflicts?from=$T&to=$T30",
    '/api/wic/coverage-gaps?days=90', "/api/wic/assign-candidates?locationCode=$locEnc&date=$T",
    "/api/wic/availableHours?from=$T&to=$T13", "/api/wic/substitutes?locationCode=$locEnc&date=$T",
    "/api/wic/backup?locationCode=$locEnc&date=$T", '/api/wicschedule/opening-hours',
    "/api/wicschedule/agents?from=$T&to=$T13", '/api/wicschedule/closed-day-duties',
    "/api/overview/wic-status?date=$T&horizon=7", "/api/dashboard/summary?date=$T",
    "/api/dashboard/wic-cards?date=$T", "/api/dashboard/teamlead-summary?date=$T",
    '/api/employees', "/api/employees/?active=true", '/api/employees/9074466',
    "/api/employees/9074466/timeline?from=$T&to=$T30", "/api/shifts?from=$T&to=$T",
    "/api/shifts/coverage?date=$T", "/api/attendance?from=$T&to=$T", "/api/attendance/agents?date=$T",
    '/api/attendance/teamleads', '/api/albalance?year=2026', "/api/alcalendar?from=$T&to=$T30",
    '/api/publicholidays?bundesland=BY&year=2026', "/api/bo-list?date=$T", "/api/breaks?date=$T",
    "/api/rtm?date=$T", "/api/sickleave?from=$T&to=$T30", "/api/sickleave/stats?from=$T&to=$T30",
    "/api/vacations?from=$T&to=$T30", "/api/vacations/active?date=$T", '/api/vacations/upcoming?days=30',
    '/api/training/topics', "/api/training/sessions?from=$T&to=$T30", "/api/vwic/daily?date=$T",
    "/api/vwic/candidates?date=$T", '/api/wic-coverage/agents', '/api/wic-coverage/wics',
    '/api/roster/locations', '/api/roster/batches', "/api/roster/missing?days=14",
    '/api/kiosk/attendance', '/api/auth/me')
# The two parameterised probes of the routes this deploy changes are added in the 'all'
# set only, so the documented 82-route list stays EXACTLY the 82 routes (27 SPA + 55 API).
if ($Set -eq 'all') {
    $apiRoutes += "/api/employees/?active=true&date=$T"
    $apiRoutes += "/api/employees/?active=true&date=$T13"
    $apiRoutes += "/api/wic/al-tracker-conflicts?from=$T&to=$T30"
    # The two routes this task actually changes, probed in the 'all' set only so the
    # documented 82-route acceptance stays exactly 82.
    $apiRoutes += "/api/sickleave/active?date=$T"
    $apiRoutes += "/api/sickleave?activeOnly=true"
}
$allRoutes = $spaRoutes + $apiRoutes

function Probe([string]$base, [string]$route) {
    try {
        $r = Invoke-WebRequest -Uri "$base$route" -WebSession $sessions[$base].session -Headers $H -UseBasicParsing -TimeoutSec 120 -SkipHttpErrorCheck
        $shape = ''
        $ct = [string]$r.Headers['Content-Type']
        $b = [string]$r.Content
        if ($b -and $ct -match 'json') {
            try { $j = $b | ConvertFrom-Json; $shape = if ($j -is [System.Array]) { "array[$($j.Count)]" } else { 'object' } } catch { $shape = 'unparsed-json' }
        } elseif ($b -and $ct -match 'html') { $shape = if ($b -match '<script') { 'spa-shell' } else { 'html' } }
        return [pscustomobject]@{ status = [int]$r.StatusCode; shape = $shape; ctype = $ct }
    } catch {
        $code = -1
        if ($_.Exception.Response) { try { $code = [int]$_.Exception.Response.StatusCode } catch { $code = -1 } }
        return [pscustomobject]@{ status = $code; shape = 'request-error'; ctype = '' }
    }
}

$rows = @()
foreach ($route in $allRoutes) {
    $l = Probe $LocalBase  $route
    $t = Probe $TunnelBase $route
    $rows += [pscustomobject]@{ route = $route; local = $l.status; localShape = $l.shape; tunnel = $t.status; tunnelShape = $t.shape }
}

$tunnelAllowedNon200 = @()   # loopback-only upstream in the proxy

$non200 = @($rows | Where-Object {
    $_.local -ne 200 -or ($_.tunnel -ne 200 -and $tunnelAllowedNon200 -notcontains $_.route)
})

$result = @{
    when = (Get-Date).ToString('o')
    base = @{ local = $LocalBase; tunnel = $TunnelBase }
    login = @{
        local  = @{ status = $sessions[$LocalBase].login;  role = $sessions[$LocalBase].role }
        tunnel = @{ status = $sessions[$TunnelBase].login; role = $sessions[$TunnelBase].role }
    }
    routes = @{
        total = $rows.Count
        local200  = @($rows | Where-Object { $_.local  -eq 200 }).Count
        tunnel200 = @($rows | Where-Object { $_.tunnel -eq 200 }).Count
        tunnelSkipped = $tunnelAllowedNon200
        non200 = $non200
    }
    all = $rows
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutFile -Encoding UTF8

"== routes @ $LocalBase / $TunnelBase =="
"login local=$($sessions[$LocalBase].login)/$($sessions[$LocalBase].role)  tunnel=$($sessions[$TunnelBase].login)/$($sessions[$TunnelBase].role)"
"routes: $($rows.Count) total"
"local:  $(@($rows | Where-Object { $_.local  -eq 200 }).Count)/$($rows.Count) x200"
"tunnel: $(@($rows | Where-Object { $_.tunnel -eq 200 }).Count)/$($rows.Count) x200"
if ($non200.Count -gt 0) {
    '--- NON-200 TABLE ---'
    $non200 | Format-Table -AutoSize | Out-String -Width 200 | Write-Output
} else {
    'NO NON-200 ROWS (no tunnel allow-list: every route must be 200 on both)'
}
"receipt: $OutFile"
exit $(if ($non200.Count -gt 0) { 1 } else { 0 })
