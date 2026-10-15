param(
    [Parameter(Mandatory=$true)][ValidateSet('before','after')][string]$Phase,
    [string]$Base     = 'http://127.0.0.1:5000',
    [string]$Kid      = 'S69307',
    [string]$ClearedCsv = 'C:\ProgramData\PulseRollback\bo-agenttask-20261015-164944\cleared-rows.csv',
    [string]$OutDir   = 'C:\GSDDashboard_secure\Tools\AgentTaskRestore20261015'
)
# READ-ONLY coverage capture (GET only) for the 19 rows being restored.
#  * /api/wic/open?date=...  -> is the centre for that day COVERED (signal 1)
#  * /api/wic/cards?date=... -> the agents the readers list for the centre (signal 1 + 2)
# The DEV KID is used; the RTM KID is never signed in.
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Data
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (!(Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# The 19 rows = cleared-rows.csv without the 10 rows of employee 9129428.
$rows = Import-Csv -LiteralPath $ClearedCsv | Where-Object { $_.EmployeeId -ne '9129428' }
if ($rows.Count -ne 19) { throw "Expected 19 rows in $ClearedCsv without 9129428, got $($rows.Count)" }

$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }
$s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$login = Invoke-RestMethod -Uri "$Base/api/auth/login" -Method Post -ContentType 'application/json' `
    -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $s -Headers $H -TimeoutSec 30
Write-Output ("login: role={0}" -f $login.role)

$openCache  = @{}
$cardsCache = @{}
$out = @()
foreach ($r in $rows) {
    $d = ([datetime]::ParseExact($r.ShiftDate.Substring(0,10), 'dd.MM.yyyy', $null)).ToString('yyyy-MM-dd')
    if (-not $openCache.ContainsKey($d)) {
        $openCache[$d]  = Invoke-RestMethod -Uri "$Base/api/wic/open?date=$d"  -WebSession $s -Headers $H -TimeoutSec 120
        $cardsCache[$d] = Invoke-RestMethod -Uri "$Base/api/wic/cards?date=$d" -WebSession $s -Headers $H -TimeoutSec 120
    }
    $centre  = $r.AgentTask
    $openDay = $openCache[$d]
    $loc = @($openDay.locations | Where-Object { $_.locationCode -eq $centre -or $_.displayName -eq $centre -or $_.city -eq $centre }) | Select-Object -First 1
    $card = @($cardsCache[$d] | Where-Object { $_.locationCode -eq $centre -or $_.displayName -eq $centre }) | Select-Object -First 1
    $out += [pscustomobject]@{
        id            = [int]$r.Id
        employeeId    = $r.EmployeeId
        date          = $d
        centre        = $centre
        rawValue      = if ([string]::IsNullOrEmpty($r.RawValue)) { $null } else { $r.RawValue }
        openStatus    = if ($loc) { $loc.coverageStatus } else { 'NOT-FOUND' }
        openEffective = if ($loc) { $loc.effectiveCoverage } else { $null }
        openScheduled = if ($loc) { $loc.scheduledCount } else { $null }
        cardStatus    = if ($card) { $card.coverageStatus } else { 'NOT-FOUND' }
        cardAgents    = if ($card) { (@($card.assignedAgents | ForEach-Object { $_.employeeId }) -join ',') } else { '' }
    }
}
$health = (Invoke-WebRequest -Uri "$Base/health" -UseBasicParsing -TimeoutSec 30).StatusCode
$result = [pscustomobject]@{ when = (Get-Date).ToString('o'); phase = $Phase; base = $Base; health = [int]$health; rows = $out }
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir "coverage-$Phase.json") -Encoding UTF8
$out | Format-Table id,employeeId,date,centre,rawValue,openStatus,openEffective,openScheduled,cardStatus,cardAgents -AutoSize | Out-String -Width 250 | Write-Output
"health: $health   receipt: $(Join-Path $OutDir "coverage-$Phase.json")"
