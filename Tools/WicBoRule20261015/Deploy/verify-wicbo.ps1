param(
    [string]$LocalBase  = 'http://127.0.0.1:5000',
    [string]$TunnelBase = 'https://d2jn94qg-5000.euw.devtunnels.ms',
    [string]$Kid        = 'S69307',
    [string]$OutDir     = 'C:\GSDDashboard_secure\.tmp-wicbo-20261015\deploy'
)
# Post-deploy acceptance for the owner rule 2026-10-15 (WIC has priority over BO).
# Read-only GETs with the DEV operator session; the RTM KID S60028 is never used.
$ErrorActionPreference = 'Stop'
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }

function Session([string]$base) {
    $s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $l = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType 'application/json' `
        -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $s -Headers $H -TimeoutSec 30
    [pscustomobject]@{ S = $s; Role = $l.role }
}

$local  = Session $LocalBase
$tunnel = Session $TunnelBase
"login local={0} tunnel={1}" -f $local.Role, $tunnel.Role

function Health([string]$base, $s) {
    (Invoke-WebRequest -Uri "$base/health" -WebSession $s -Headers $H -UseBasicParsing -TimeoutSec 30).StatusCode
}
$hLocal  = Health $LocalBase  $local.S
$hTunnel = Health $TunnelBase $tunnel.S
"health local=$hLocal tunnel=$hTunnel"

function Open([string]$base, $s, [string]$date) {
    Invoke-RestMethod -Uri "$base/api/wic/open?date=$date&horizon=1" -WebSession $s -Headers $H -TimeoutSec 120
}
function Cards([string]$base, $s, [string]$date) {
    Invoke-RestMethod -Uri "$base/api/wic/cards?date=$date" -WebSession $s -Headers $H -TimeoutSec 180
}

# ── Mülheim 2026-10-29 (row 57736: RTM's own BO day) ────────────────────────────────
$open = Open $LocalBase $local.S '2026-10-29'
$mul  = $open[0].locations | Where-Object { $_.displayName -eq 'Mülheim' } | Select-Object -First 1
$mulCard = (Cards $LocalBase $local.S '2026-10-29') | Where-Object { $_.displayName -eq 'Mülheim' } | Select-Object -First 1
"Mulheim 2026-10-29 open: status={0} scheduled={1} absent={2} effective={3} minReq={4} isOpen={5}" -f `
    $mul.coverageStatus, $mul.scheduledCount, $mul.absentCount, $mul.effectiveCoverage, $mul.minRequired, $mul.isOpen
"Mulheim 2026-10-29 cards: status={0} agents={1}" -f $mulCard.coverageStatus,
    (@($mulCard.assignedAgents | ForEach-Object { $_.employeeId }) -join ',')

# ── Mülheim 2026-10-22 (same agent, PLAN BO Thursday -> now WIC) ────────────────────
$open22 = Open $LocalBase $local.S '2026-10-22'
$mul22  = $open22[0].locations | Where-Object { $_.displayName -eq 'Mülheim' } | Select-Object -First 1
"Mulheim 2026-10-22 open: status={0} scheduled={1} effective={2} minReq={3}" -f `
    $mul22.coverageStatus, $mul22.scheduledCount, $mul22.effectiveCoverage, $mul22.minRequired

# ── Pfaffenhofen November (Binod's manual BO days 16.-27.11.) ───────────────────────
# 10.-13.11. are REAL WIC_DUTY days for 9129428 (ASSIGN, 2026-10-15 13:00) and are
# deliberately NOT part of the "only 9135516" expectation.
$pfaff = 'DE~85276~Pfaffenhofen~Draht 7'
$pfRows = @()
foreach ($d in @('2026-11-16','2026-11-17','2026-11-18','2026-11-19','2026-11-20','2026-11-23','2026-11-24','2026-11-25','2026-11-26','2026-11-27')) {
    $card = (Cards $LocalBase $local.S $d) | Where-Object { $_.locationCode -eq $pfaff } | Select-Object -First 1
    $ids = @($card.assignedAgents | ForEach-Object { $_.employeeId })
    $pfRows += [pscustomobject]@{
        date = $d; status = $card.coverageStatus; agents = ($ids -join ',')
        only9135516 = (($ids -join ',') -eq '9135516'); has9129428 = ($ids -contains '9129428')
    }
}
$pfRows | Format-Table -AutoSize | Out-String -Width 160 | Write-Output

# ── Helmstedt 2026-10-16 ────────────────────────────────────────────────────────────
$helm = (Cards $LocalBase $local.S '2026-10-16') | Where-Object { $_.displayName -eq 'Helmstedt' } | Select-Object -First 1
"Helmstedt 2026-10-16: status={0} agents={1}" -f $helm.coverageStatus,
    (@($helm.assignedAgents | ForEach-Object { $_.employeeId }) -join ',')

# ── A sample of the newly covered plan-BO days ──────────────────────────────────────
$sample = @()
foreach ($x in @(
    @{ c='Saarbrücken'; d='2026-10-16' }, @{ c='Neuss'; d='2026-10-28' },
    @{ c='München';    d='2026-10-20' }, @{ c='Stadland'; d='2026-10-21' },
    @{ c='Mülheim';    d='2026-11-05' }, @{ c='Arnsberg'; d='2026-11-04' })) {
    $card = (Cards $LocalBase $local.S $x.d) | Where-Object { $_.displayName -eq $x.c } | Select-Object -First 1
    if (!$card) { continue }
    $sample += [pscustomobject]@{ centre = $x.c; date = $x.d; status = $card.coverageStatus
        agents = (@($card.assignedAgents | ForEach-Object { $_.employeeId }) -join ',') }
}
$sample | Format-Table -AutoSize | Out-String -Width 160 | Write-Output

# ── Tunnel: same reading for the two rows above ─────────────────────────────────────
$mulT = (Open $TunnelBase $tunnel.S '2026-10-29')[0].locations | Where-Object { $_.displayName -eq 'Mülheim' } | Select-Object -First 1
$pfT  = (Cards $TunnelBase $tunnel.S '2026-11-16') | Where-Object { $_.locationCode -eq $pfaff } | Select-Object -First 1
"tunnel Mulheim 2026-10-29: status={0} effective={1} minReq={2}" -f $mulT.coverageStatus, $mulT.effectiveCoverage, $mulT.minRequired
"tunnel Pfaffenhofen 2026-11-16: status={0} agents={1}" -f $pfT.coverageStatus,
    (@($pfT.assignedAgents | ForEach-Object { $_.employeeId }) -join ',')

$badPf = @($pfRows | Where-Object { $_.has9129428 -or -not $_.only9135516 })
$pass = ($hLocal -eq 200) -and ($hTunnel -eq 200) -and
        ($mul.coverageStatus -eq 'UNCOVERED') -and (@($mulCard.assignedAgents).Count -eq 0) -and
        ($mul22.coverageStatus -eq 'COVERED') -and
        ($helm.coverageStatus -eq 'COVERED') -and ($badPf.Count -eq 0)

$result = [pscustomobject]@{
    when = (Get-Date).ToString('o')
    health = @{ local = $hLocal; tunnel = $hTunnel }
    mulheim_20261029 = $mul
    mulheim_20261029_card = [pscustomobject]@{ status = $mulCard.coverageStatus; agents = @($mulCard.assignedAgents | ForEach-Object { $_.employeeId }) }
    mulheim_20261022 = $mul22
    pfaffenhofen = $pfRows
    helmstedt = [pscustomobject]@{ date = '2026-10-16'; status = $helm.coverageStatus }
    sample = $sample
    pass = $pass
}
if (!(Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutDir 'wicbo-acceptance.json') -Encoding UTF8
"PASS: $pass"
if (!$pass) { exit 1 }
