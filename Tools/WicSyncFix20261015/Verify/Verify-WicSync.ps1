# WIC shift-plan sync — acceptance verification (READ-ONLY against the running API).
#
# Compares, for every OPEN WIC centre and every day in [today .. 2027-02-28]:
#     GET /api/wic/open?date=..&horizon=1    (SIGNAL 1: on-site WicShiftEntries rows)
#     GET /api/wic/cards?date=..             (the ShiftEntries reading; country=ALL)
# A day+centre where the two coverage statuses differ is a MISMATCH (the defect this task
# repairs). It also records the named acceptance checks (Wesel's four Thursdays, Mülheim
# 29.10., Pfaffenhofen 16.-27.11., Helmstedt 16.10.) and /health.
#
# Login is the DEV operator KID S69307. The RTM KID S60028 is never used.
# No write, no POST other than the login.
param(
    [string]$Base = 'http://127.0.0.1:5000',
    [string]$OutFile = 'C:\GSDDashboard_secure\Tools\WicSyncFix20261015\Verify\wicsync-verify.json',
    [string]$MismatchCsv = 'C:\GSDDashboard_secure\Tools\WicSyncFix20261015\Verify\wicsync-mismatches.csv',
    [string]$From = '2026-10-15',
    [string]$To = '2027-02-28'
)
$ErrorActionPreference = 'Stop'
$Inv = [System.Globalization.CultureInfo]::InvariantCulture
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$login = Invoke-RestMethod -Uri "$Base/api/auth/login" -Method Post -ContentType 'application/json' `
    -Body (@{ kid = 'S69307' } | ConvertTo-Json) -WebSession $session -Headers $H -TimeoutSec 30
Write-Output "login KID=S69307 role=$($login.role)  -- RTM KID S60028 not used"

$health = Invoke-WebRequest -Uri "$Base/health" -WebSession $session -Headers $H -TimeoutSec 30 -SkipHttpErrorCheck
Write-Output "/health -> $($health.StatusCode)"

function Get-Open([string]$date) {
    return Invoke-RestMethod -Uri "$Base/api/wic/open?date=$date&horizon=1" -WebSession $session -Headers $H -TimeoutSec 90
}
function Get-Cards([string]$date) {
    return @(Invoke-RestMethod -Uri "$Base/api/wic/cards?date=$date" -WebSession $session -Headers $H -TimeoutSec 90)
}

$start = [datetime]::ParseExact($From, 'yyyy-MM-dd', $Inv)
$end = [datetime]::ParseExact($To, 'yyyy-MM-dd', $Inv)

$mismatches = New-Object System.Collections.ArrayList
$openCentreDays = 0
$days = 0

for ($d = $start; $d -le $end; $d = $d.AddDays(1)) {
    $ds = $d.ToString('yyyy-MM-dd', $Inv)
    $open = Get-Open $ds
    $cards = Get-Cards $ds
    $cardByCode = @{}
    foreach ($c in $cards) { $cardByCode[$c.locationCode] = $c }
    $days++
    foreach ($loc in $open[0].locations) {
        if (-not $loc.isOpen) { continue }
        $openCentreDays++
        $card = $cardByCode[$loc.locationCode]
        $cardStatus = if ($card) { $card.coverageStatus } else { 'MISSING' }
        if ($cardStatus -ne $loc.coverageStatus) {
            # Two very different things can make the two statuses differ:
            #   * THE DEFECT: one surface says the centre is staffed, the other says it is
            #     not (open UNCOVERED / cards COVERED-PARTIAL, or the reverse);
            #   * a GRADE difference only: both say "staffed", but /api/wic/open grades by
            #     the number of agents against MinRequired while /api/wic/cards measures
            #     MINUTES against the centre's open window (PARTIAL vs COVERED).
            $staffed = { param($s) $s -eq 'COVERED' -or $s -eq 'PARTIAL' }
            $openStaffed = & $staffed $loc.coverageStatus
            $cardStaffed = & $staffed $cardStatus
            $class = if ($openStaffed -ne $cardStaffed) {
                if ($openStaffed) { 'OPEN_STAFFED_CARDS_NOT' } else { 'OPEN_NOT_CARDS_STAFFED' }
            } else { 'GRADE_ONLY' }
            [void]$mismatches.Add([pscustomobject]@{
                    date            = $ds
                    dayOfWeek       = $d.DayOfWeek.ToString()
                    locationCode    = $loc.locationCode
                    displayName     = $loc.displayName
                    openStatus      = $loc.coverageStatus
                    cardsStatus     = $cardStatus
                    class           = $class
                    openScheduled   = $loc.scheduledCount
                    openEffective   = $loc.effectiveCoverage
                    minRequired     = $loc.minRequired
                    openIntervals   = (($loc.openIntervals | ForEach-Object { "$($_.openTime)-$($_.closeTime)" }) -join '+')
                    cardsEvidence   = if ($card) { $card.coverageEvidence } else { '' }
                    cardsAgents     = if ($card) { ($card.assignedAgents | ForEach-Object { $_.employeeId }) -join '|' } else { '' }
                    cardsScheduled  = if ($card) { @($card.assignedAgents).Count } else { 0 }
                })
        }
    }
}

Write-Output "days=$days  open centre-days=$openCentreDays  mismatches=$($mismatches.Count)"
$mismatches | Group-Object class | Sort-Object Count -Descending | ForEach-Object { Write-Output "  $($_.Name) = $($_.Count)" }
$mismatches | Where-Object { $_.class -ne 'GRADE_ONLY' } | Sort-Object date, displayName |
    Format-Table date, displayName, class, openStatus, cardsStatus, openScheduled, openEffective, minRequired, cardsEvidence, cardsAgents -AutoSize |
    Out-String -Width 220 | Write-Output

# ── named acceptance checks (match on the location code so "Wesel" cannot match
#    "Münster ~ Weseler Str. 480") ────────────────────────────────────────────────────────
function Check-Day([string]$date, [string]$codeLike) {
    $open = Get-Open $date
    $cards = Get-Cards $date
    $loc = $open[0].locations | Where-Object { $_.locationCode -like $codeLike }
    $card = $cards | Where-Object { $_.locationCode -like $codeLike }
    return [pscustomobject]@{
        date          = $date
        centre        = $loc.displayName
        locationCode  = $loc.locationCode
        isOpen        = $loc.isOpen
        openIntervals = (($loc.openIntervals | ForEach-Object { "$($_.openTime)-$($_.closeTime)" }) -join '+')
        openStatus    = $loc.coverageStatus
        openScheduled = $loc.scheduledCount
        openEffective = $loc.effectiveCoverage
        minRequired   = $loc.minRequired
        cardsStatus   = $card.coverageStatus
        cardsEvidence = $card.coverageEvidence
        cardsAgents   = @($card.assignedAgents | ForEach-Object { $_.employeeId })
        mismatch      = ($card.coverageStatus -ne $loc.coverageStatus)
    }
}

$wesel = @()
foreach ($ds in '2026-11-05', '2026-11-12', '2026-11-19', '2026-11-26') { $wesel += Check-Day $ds '*~Wesel~*' }
$muelheim = Check-Day '2026-10-29' '*~Mülheim~*'
$helmstedt = Check-Day '2026-10-16' '*~Helmstedt~*'

# Pfaffenhofen: the ten days on which 9129428 carries a BO day (2026-11-16 .. 2026-11-27, weekdays)
$pfaff = @()
for ($d = [datetime]::ParseExact('2026-11-16', 'yyyy-MM-dd', $Inv); $d -le [datetime]::ParseExact('2026-11-27', 'yyyy-MM-dd', $Inv); $d = $d.AddDays(1)) {
    if ($d.DayOfWeek -in 'Saturday', 'Sunday') { continue }
    $pfaff += Check-Day ($d.ToString('yyyy-MM-dd', $Inv)) '*~Pfaffenhofen~*'
}

$receipt = [pscustomobject]@{
    generatedAt       = (Get-Date).ToString('o')
    base              = $Base
    windowFrom        = $From
    windowTo          = $To
    loginKid          = 'S69307'
    healthStatus      = $health.StatusCode
    days              = $days
    openCentreDays    = $openCentreDays
    mismatchCount     = $mismatches.Count
    mismatchClasses   = ($mismatches | Group-Object class | ForEach-Object { [pscustomobject]@{ class = $_.Name; count = $_.Count } })
    staffedMismatchCount = @($mismatches | Where-Object { $_.class -ne 'GRADE_ONLY' }).Count
    mismatches        = $mismatches
    weselThursdays    = $wesel
    muelheim20261029  = $muelheim
    helmstedt20261016 = $helmstedt
    pfaffenhofenBoDays = $pfaff
}
$dir = Split-Path -Parent $OutFile
if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutFile -Encoding UTF8
$mismatches | Sort-Object date, displayName | Export-Csv -LiteralPath $MismatchCsv -NoTypeInformation -Encoding UTF8

Write-Output ""
Write-Output "Wesel: $($wesel | ForEach-Object { "$($_.date)=$($_.openStatus)/$($_.cardsStatus)" } | Join-String -Separator '  ')"
Write-Output "Mülheim 29.10: open=$($muelheim.openStatus) cards=$($muelheim.cardsStatus) sched=$($muelheim.openScheduled) eff=$($muelheim.openEffective) min=$($muelheim.minRequired) agents=$($muelheim.cardsAgents -join ',')"
Write-Output "Helmstedt 16.10: open=$($helmstedt.openStatus) cards=$($helmstedt.cardsStatus) agents=$($helmstedt.cardsAgents -join ',')"
Write-Output "Pfaffenhofen 16.-27.11.: $($pfaff | ForEach-Object { "$($_.date)=$($_.cardsStatus)[$($_.cardsAgents -join '+')]" } | Join-String -Separator ' ')"
Write-Output "receipt: $OutFile"
Write-Output "mismatches CSV: $MismatchCsv"


