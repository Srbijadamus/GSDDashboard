param(
    [string]$LocalBase = 'http://127.0.0.1:5000',
    [string]$Kid       = 'S69307',
    [string]$OutDir    = 'C:\GSDDashboard_secure\.tmp-bo-agenttask-deploy',
    [string[]]$Dates   = @(
        '2026-11-16','2026-11-17','2026-11-18','2026-11-19','2026-11-20',
        '2026-11-23','2026-11-24','2026-11-25','2026-11-26','2026-11-27'
    ),
    [string]$HelmstedtDate = '2026-10-16'
)
# Post-deploy acceptance for the WIC-duty rule:
#   * /api/wic/cards for Pfaffenhofen on the BO days in November must list ONLY 9135516
#     (9129428 was a BO/WORKING row and is no longer WIC cover);
#   * Helmstedt 2026-10-16 must still be COVERED (the rule must not remove real cover);
#   * /health must be 200.
# Read-only GETs with a DEV session; the RTM KID is never used.
$ErrorActionPreference = 'Continue'
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }
$s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$login = Invoke-RestMethod -Uri "$LocalBase/api/auth/login" -Method Post -ContentType 'application/json' `
    -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $s -Headers $H -TimeoutSec 30
Write-Output ("login: role={0}" -f $login.role)

$health = Invoke-WebRequest -Uri "$LocalBase/health" -UseBasicParsing -TimeoutSec 30
Write-Output ("health: {0}" -f $health.StatusCode)

$pfaff = 'DE~85276~Pfaffenhofen~Draht 7'
$rows = @()
foreach ($d in $Dates) {
    $cards = Invoke-RestMethod -Uri "$LocalBase/api/wic/cards?date=$d" -WebSession $s -Headers $H -TimeoutSec 120
    $card = $cards | Where-Object { $_.locationCode -eq $pfaff } | Select-Object -First 1
    $ids = @($card.assignedAgents | ForEach-Object { $_.employeeId }) -join ','
    $rows += [pscustomobject]@{
        date = $d; status = $card.coverageStatus; agents = $ids
        only9135516 = ($ids -eq '9135516')
        has9129428 = ($ids -like '*9129428*')
    }
}
$rows | Format-Table -AutoSize | Out-String -Width 160 | Write-Output

$helm = Invoke-RestMethod -Uri "$LocalBase/api/wic/cards?date=$HelmstedtDate" -WebSession $s -Headers $H -TimeoutSec 120
$h = $helm | Where-Object { $_.displayName -eq 'Helmstedt' } | Select-Object -First 1
Write-Output ("Helmstedt {0}: status={1} agents={2}" -f $HelmstedtDate, $h.coverageStatus,
    (@($h.assignedAgents | ForEach-Object { $_.employeeId }) -join ','))

$bad = @($rows | Where-Object { $_.has9129428 -or -not $_.only9135516 })
$pass = ($health.StatusCode -eq 200) -and ($bad.Count -eq 0) -and ($h.coverageStatus -eq 'COVERED')

$result = [pscustomobject]@{
    when = (Get-Date).ToString('o')
    base = $LocalBase
    health = [int]$health.StatusCode
    pfaffenhofen = $rows
    helmstedt = [pscustomobject]@{ date = $HelmstedtDate; status = $h.coverageStatus }
    pass = $pass
}
if (!(Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'wic-permission-check.json') -Encoding UTF8
Write-Output ("PASS: {0}" -f $pass)
if (!$pass) { exit 1 }
