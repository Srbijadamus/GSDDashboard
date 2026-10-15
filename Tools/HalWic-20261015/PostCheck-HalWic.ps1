param(
    [string]$LocalBase  = 'http://127.0.0.1:5000',
    [string]$TunnelBase = 'https://d2jn94qk-5000.euw.devtunnels.ms',
    [string]$OutDir     = 'C:\GSDDashboard_secure\.tmp-halwic-deploy',
    [string]$Kid        = 'S69307'
)
# Post-deploy acceptance for the 2026-10-15 HAL/WIC task (requirement 7).
# Read-only GETs only. Signs in with the DEV operator KID, never the RTM KID.
$ErrorActionPreference = 'Stop'
$TunnelBase = 'https://d2jn94qg-5000.euw.devtunnels.ms'
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }
if (!(Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

function New-Session([string]$base) {
    $s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $l = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType 'application/json' `
        -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $s -Headers $H -TimeoutSec 30
    return @{ session = $s; role = $l.role }
}
function Get-Json([string]$base, $session, [string]$route) {
    return (Invoke-WebRequest -Uri "$base$route" -WebSession $session -Headers $H -UseBasicParsing -TimeoutSec 120 -SkipHttpErrorCheck).Content | ConvertFrom-Json
}

$local  = New-Session $LocalBase
$tunnel = New-Session $TunnelBase
$report = [ordered]@{ when = (Get-Date).ToString('o'); login = @{ local = $local.role; tunnel = $tunnel.role } }

# ── 1. Helmstedt 2026-10-16 ────────────────────────────────────────────────────────────
$openL = Get-Json $LocalBase  $local.session  '/api/wic/open?date=2026-10-16&horizon=1'
$openT = Get-Json $TunnelBase $tunnel.session '/api/wic/open?date=2026-10-16&horizon=1'
$rowL = @($openL[0].locations | Where-Object { $_.displayName -eq 'Helmstedt' })[0]
$rowT = @($openT[0].locations | Where-Object { $_.displayName -eq 'Helmstedt' })[0]
$wic16 = @(Get-Json $LocalBase $local.session '/api/wic?from=2026-10-16&to=2026-10-16&location=Helmstedt')
$cards16 = Get-Json $LocalBase $local.session '/api/wic/cards?date=2026-10-16'
$helmCard = @($cards16 | Where-Object { $_.displayName -eq 'Helmstedt' })[0]

$report.helmstedt1610 = [ordered]@{
    openLocal  = @{ isOpen = $rowL.isOpen; status = $rowL.coverageStatus; scheduled = $rowL.scheduledCount; absent = $rowL.absentCount; effective = $rowL.effectiveCoverage; minRequired = $rowL.minRequired; intervals = $rowL.openIntervals }
    openTunnel = @{ status = $rowT.coverageStatus; scheduled = $rowT.scheduledCount; effective = $rowT.effectiveCoverage }
    wicRows = @($wic16 | ForEach-Object { [ordered]@{ employeeId = $_.employeeId; isOnSite = $_.isOnSite; workingShift = $_.workingShift; agentStatus = $_.agentStatus } })
    cardAgents = @($helmCard.assignedAgents | ForEach-Object { [ordered]@{ employeeId = $_.employeeId; workingShift = $_.workingShift; coverageMatch = $_.coverageMatch; coveredMinutes = $_.coveredMinutes; totalOpenMinutes = $_.totalOpenMinutes } })
    cardStatus = $helmCard.coverageStatus
}

# ── 2. Pfaffenhofen on the BO days in November ─────────────────────────────────────────
$boDays = @('2026-11-16','2026-11-17','2026-11-18','2026-11-19','2026-11-20','2026-11-23','2026-11-24','2026-11-25','2026-11-26','2026-11-27')
$pfaff = @()
foreach ($d in $boDays) {
    $o = Get-Json $LocalBase $local.session "/api/wic/open?date=$d&horizon=1"
    $r = @($o[0].locations | Where-Object { $_.displayName -eq 'Pfaffenhofen' })[0]
    $pfaff += [ordered]@{ date = $d; status = $r.coverageStatus; scheduled = $r.scheduledCount; effective = $r.effectiveCoverage; absent = $r.absentCount }
}
$wicNov = @(Get-Json $LocalBase $local.session '/api/wic?from=2026-11-01&to=2026-11-30&location=Pfaffenhofen')
$cardsNov = Get-Json $LocalBase $local.session '/api/wic/cards?date=2026-11-16'
$pfaffCard = @($cardsNov | Where-Object { $_.displayName -eq 'Pfaffenhofen' })[0]
$report.pfaffenhofenNovember = [ordered]@{
    boDays = $pfaff
    wicRowsOnBoDays = @($wicNov | Where-Object { $boDays -contains $_.shiftDate } | ForEach-Object { [ordered]@{ date = $_.shiftDate; employeeId = $_.employeeId; isOnSite = $_.isOnSite; workingShift = $_.workingShift } })
    cardAgents20261116 = @($pfaffCard.assignedAgents | ForEach-Object { [ordered]@{ employeeId = $_.employeeId; workingShift = $_.workingShift; coverageMatch = $_.coverageMatch } })
    cardStatus20261116 = $pfaffCard.coverageStatus
}

# ── 3. Conflicts and health ────────────────────────────────────────────────────────────
$confL = @(Get-Json $LocalBase  $local.session  '/api/wic/conflicts?from=2026-10-15&to=2026-11-30')
$confT = @(Get-Json $TunnelBase $tunnel.session '/api/wic/conflicts?from=2026-10-15&to=2026-11-30')
$conf1610L = @(Get-Json $LocalBase $local.session '/api/wic/conflicts?from=2026-10-16&to=2026-10-16')
$report.conflicts = [ordered]@{
    range_20261015_20261130_local = $confL.Count
    range_20261015_20261130_tunnel = $confT.Count
    day_20261016_local = $conf1610L.Count
}
$report.health = [ordered]@{
    local  = (Invoke-WebRequest -Uri "$LocalBase/health"  -UseBasicParsing -TimeoutSec 30 -SkipHttpErrorCheck).StatusCode
    tunnel = (Invoke-WebRequest -Uri "$TunnelBase/health" -Headers $H -UseBasicParsing -TimeoutSec 60 -SkipHttpErrorCheck).StatusCode
}

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutDir 'postcheck.json') -Encoding UTF8

"=== HELMSTEDT 2026-10-16 ==="
"open (local) : status=$($rowL.coverageStatus) scheduled=$($rowL.scheduledCount) absent=$($rowL.absentCount) effective=$($rowL.effectiveCoverage)/$($rowL.minRequired) intervals=$((@($rowL.openIntervals) | ForEach-Object { "$($_.openTime)-$($_.closeTime)" }) -join ',')"
"open (tunnel): status=$($rowT.coverageStatus) scheduled=$($rowT.scheduledCount) effective=$($rowT.effectiveCoverage)"
"WIC rows     : " + ((@($wic16) | ForEach-Object { "$($_.employeeId) onSite=$($_.isOnSite) ws=$($_.workingShift)" }) -join ' | ')
"card ($($helmCard.coverageStatus)): " + ((@($helmCard.assignedAgents) | ForEach-Object { "$($_.employeeId) ws=$($_.workingShift) match=$($_.coverageMatch) $($_.coveredMinutes)/$($_.totalOpenMinutes)min" }) -join ' | ')
""
"=== PFAFFENHOFEN, Binod BO days ==="
foreach ($p in $pfaff) { "$($p.date): $($p.status) scheduled=$($p.scheduled) effective=$($p.effective) absent=$($p.absent)" }
"WIC rows on those days: " + ((@($wicNov | Where-Object { $boDays -contains $_.shiftDate }) | ForEach-Object { "$($_.shiftDate) $($_.employeeId) onSite=$($_.isOnSite)" }) -join ' | ')
"card 2026-11-16 ($($pfaffCard.coverageStatus)): " + ((@($pfaffCard.assignedAgents) | ForEach-Object { "$($_.employeeId) ws=$($_.workingShift) match=$($_.coverageMatch)" }) -join ' | ')
""
"=== CONFLICTS / HEALTH ==="
"conflicts 2026-10-15..2026-11-30: local=$($confL.Count) tunnel=$($confT.Count); 2026-10-16: $($conf1610L.Count)"
"health: local=$($report.health.local) tunnel=$($report.health.tunnel)"
"receipt: $(Join-Path $OutDir 'postcheck.json')"
