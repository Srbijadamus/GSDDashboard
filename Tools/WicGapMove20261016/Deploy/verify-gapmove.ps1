param(
    [string]$LocalBase  = 'http://127.0.0.1:5000',
    [string]$TunnelBase = 'https://d2jn94qg-5000.euw.devtunnels.ms',
    [string]$OutFile    = 'C:\GSDDashboard_secure\Tools\WicGapMove20261016\Deploy\gapmove-acceptance.json',
    [string]$Kid        = 'S69307',
    [int]$ExpectedProposals = 111,
    [int]$ExpectedWarnings  = 5,
    # Off for the pre-deploy baseline run (the live DLL has no Source* fields yet): the
    # proposal list is then still captured, which is what the before/after comparison uses.
    [switch]$EnforceWarningFields
)
# Post-deploy acceptance for the 2026-10-16 "consequence of the move" task.
# READ-ONLY: GET routes only, DEV operator KID only (never the RTM KID S60028), no writes.
#
#   /health                                                       -> 200
#   /api/wic/coverage-gaps?days=90                                -> proposals 111, warnings 5
#       Hannover 2026-11-02..06                                   -> 9124697 warned, Helmstedt 0/1
#   /api/wic/al-tracker-conflicts                                 -> Regensburg 2026-10-20 conflict shown
#   /api/wic/open + /api/wic/cards, Regensburg 2026-10-20         -> still present, Shift Plan wins
$ErrorActionPreference = 'Continue'
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }
$failures = New-Object System.Collections.ArrayList

function Get-Session([string]$base) {
    $s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $st = -1; $role = ''
    try {
        $l = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType 'application/json' `
            -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $s -Headers $H -TimeoutSec 30
        $st = 200; $role = $l.role
    } catch { $role = "login-failed: $($_.Exception.Message)" }
    @{ session = $s; login = $st; role = $role }
}

function Get-Json([string]$base, $sess, [string]$route) {
    $r = Invoke-WebRequest -Uri "$base$route" -WebSession $sess -Headers $H -UseBasicParsing -TimeoutSec 240 -SkipHttpErrorCheck
    if ([int]$r.StatusCode -ne 200) { throw "HTTP $($r.StatusCode) for $route" }
    return ($r.Content | ConvertFrom-Json)
}

$results = @()
foreach ($base in @($LocalBase, $TunnelBase)) {
    $s = Get-Session $base
    $issues = New-Object System.Collections.ArrayList
    $health = -1
    try { $health = [int](Invoke-WebRequest -Uri "$base/health" -UseBasicParsing -TimeoutSec 30).StatusCode } catch { }

    $gaps = $null; $proposals = @(); $warnings = @(); $hannover = @()
    try {
        $gaps = Get-Json $base $s.session '/api/wic/coverage-gaps?days=90'
        $idx = 0
        foreach ($g in $gaps.gaps) {
            foreach ($a in $g.availableFloaters) {
                $proposals += [pscustomobject]@{
                    gapLocation = $g.displayName; gapDate = $g.date; index = $idx
                    employeeId = $a.employeeId; taskLabel = $a.taskLabel
                    sourceLocation = $a.sourceLocation; after = $a.sourcePresentAfter
                    required = $a.sourceRequired; belowMinimum = [bool]$a.sourceBelowMinimum
                }
                if ($a.sourceBelowMinimum) {
                    $warnings += [pscustomobject]@{
                        gapLocation = $g.displayName; gapDate = $g.date; employeeId = $a.employeeId
                        taskLabel = $a.taskLabel; sourceLocation = $a.sourceLocation
                        after = $a.sourcePresentAfter; required = $a.sourceRequired
                    }
                }
                $idx++
            }
        }
        $hannover = @($gaps.gaps | Where-Object {
            $_.displayName -eq 'Hannover' -and $_.date -ge '2026-11-02' -and $_.date -le '2026-11-06'
        } | ForEach-Object {
            [pscustomobject]@{
                date = $_.date; present = $_.present; required = $_.required; category = $_.category
                floaters = @($_.availableFloaters | ForEach-Object {
                    [pscustomobject]@{ employeeId = $_.employeeId; taskLabel = $_.taskLabel
                        sourceLocation = $_.sourceLocation; after = $_.sourcePresentAfter
                        required = $_.sourceRequired; belowMinimum = [bool]$_.sourceBelowMinimum }
                })
            }
        })
    } catch { [void]$issues.Add("coverage-gaps: $($_.Exception.Message)") }

    $proposalCount = $proposals.Count
    $warningCount  = $warnings.Count
    if ($proposalCount -ne $ExpectedProposals) { [void]$issues.Add("proposals $proposalCount <> $ExpectedProposals") }
    if ($EnforceWarningFields -and $warningCount -ne $ExpectedWarnings) { [void]$issues.Add("warnings $warningCount <> $ExpectedWarnings") }

    $hannoverOk = $false
    if ($hannover.Count -eq 5) {
        $hannoverOk = @($hannover | Where-Object {
            @($_.floaters | Where-Object { $_.employeeId -eq '9124697' }).Count -eq 1
        }).Count -eq 5
        if ($EnforceWarningFields) {
            $hannoverOk = $hannoverOk -and (@($hannover | Where-Object {
                @($_.floaters | Where-Object {
                    $_.employeeId -eq '9124697' -and $_.belowMinimum -and
                    $_.sourceLocation -eq 'Helmstedt' -and $_.after -eq 0 -and $_.required -eq 1
                }).Count -eq 1
            }).Count -eq 5)
        }
    }
    if (!$hannoverOk) { [void]$issues.Add('Hannover 2026-11-02..06: 9124697 with Helmstedt 0/1 not on all five days') }

    # Regensburg 2026-10-20: the case itself must still be present (a manual WIC_DUTY at
    # Regensburg plus the on-site WIC row, /api/wic/open COVERED with 1 agent). The
    # AL-TRACKER conflict ("sukob prikazan") is SHOWN when the tracker row still exists;
    # if the AL Tracker owner has since corrected/removed it (the warning's whole point),
    # the conflict list is legitimately empty and that is recorded, not failed.
    $alCount = -1; $alRegensburg = $false
    try {
        $al = Get-Json $base $s.session '/api/wic/al-tracker-conflicts?from=2026-10-16&to=2026-11-30'
        $alCount = $al.count
        $alRegensburg = @($al.conflicts | Where-Object {
            $_.employeeId -eq '9130657' -and $_.date -eq '2026-10-20' -and $_.alTrackerEntryId -eq 1377 -and
            $_.shiftType -eq 'WIC_DUTY' -and $_.agentTask -eq 'Regensburg'
        }).Count -eq 1
    } catch { [void]$issues.Add("al-tracker-conflicts: $($_.Exception.Message)") }

    $regOpen = $null; $regCard = $null
    try {
        $locEnc = [uri]::EscapeDataString('DE~93049~Regensburg~Lilienthalstraße 7')
        $open = Get-Json $base $s.session "/api/wic/open?date=2026-10-20&locationCode=$locEnc"
        # /api/wic/open answers one object per requested date, each carrying its locations[].
        $regOpen = @($open | ForEach-Object { $_.locations } | Where-Object { $_.displayName -eq 'Regensburg' })[0]
        $cards = Get-Json $base $s.session '/api/wic/cards?date=2026-10-20'
        $regCard = @($cards | Where-Object { $_.displayName -eq 'Regensburg' })[0]
    } catch { [void]$issues.Add("Regensburg open/cards: $($_.Exception.Message)") }
    if ($regOpen -eq $null -or $regCard -eq $null) {
        [void]$issues.Add('Regensburg 2026-10-20 missing from open/cards')
    } elseif (!$regOpen.isOpen -or $regOpen.coverageStatus -ne 'COVERED' -or $regOpen.scheduledCount -ne 1) {
        [void]$issues.Add("Regensburg 2026-10-20 open is $($regOpen.coverageStatus)/$($regOpen.scheduledCount), expected COVERED/1")
    }

    if ($health -ne 200) { [void]$issues.Add("health $health") }

    $results += [pscustomobject]@{
        base = $base
        login = @{ status = $s.login; role = $s.role }
        health = $health
        gapsWindow = if ($gaps) { @{ from = $gaps.from; to = $gaps.to; total = $gaps.gaps.Count
            critical = $gaps.criticalCount; warning = $gaps.warningCount; notRecorded = $gaps.notRecorded.Count } } else { $null }
        proposals = $proposalCount
        proposalsWithWarning = $warningCount
        warnings = $warnings
        hannover = $hannover
        hannoverOk = $hannoverOk
        alTrackerConflict = @{ count = $alCount; regensburg1020 = $alRegensburg }
        regensburg1020 = @{
            open = if ($regOpen) { @{ isOpen = $regOpen.isOpen; status = $regOpen.coverageStatus
                scheduled = $regOpen.scheduledCount; absent = $regOpen.absentCount
                effective = $regOpen.effectiveCoverage; minRequired = $regOpen.minRequired } } else { $null }
            card = if ($regCard) { @{ status = $regCard.coverageStatus; evidence = $regCard.coverageEvidence
                agents = @($regCard.assignedAgents | ForEach-Object { $_.employeeId }) } } else { $null }
        }
        allProposals = $proposals
        issues = @($issues)
        pass = ($issues.Count -eq 0)
    }
    foreach ($i in $issues) { [void]$failures.Add("$base : $i") }
}

$report = @{
    when = (Get-Date).ToString('o')
    enforceWarningFields = [bool]$EnforceWarningFields
    expected = @{ proposals = $ExpectedProposals; warnings = $ExpectedWarnings }
    results = $results
    failures = @($failures)
    pass = ($failures.Count -eq 0)
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutFile -Encoding UTF8

foreach ($r in $results) {
    "== $($r.base) =="
    "login  : $($r.login.status)/$($r.login.role)   health: $($r.health)"
    "gaps   : $(if ($r.gapsWindow) { "$($r.gapsWindow.total) rows, $($r.gapsWindow.critical) critical, $($r.gapsWindow.warning) unstaffed, window $($r.gapsWindow.from)..$($r.gapsWindow.to)" } else { 'n/a' })"
    "proposals: $($r.proposals) (with warning: $($r.proposalsWithWarning))"
    "hannover 2026-11-02..06 with 9124697: $($r.hannoverOk)"
    "regensburg 2026-10-20: open=$($r.regensburg1020.open.status) scheduled=$($r.regensburg1020.open.scheduled) card=$($r.regensburg1020.card.status) alTrackerConflict=$($r.alTrackerConflict.regensburg1020) (al total $($r.alTrackerConflict.count))"
    if ($r.warnings.Count -gt 0) {
        '--- proposals with a warning ---'
        $r.warnings | ForEach-Object { "  $($_.gapDate) $($_.gapLocation) -> $($_.employeeId) ($($_.taskLabel) -> $($_.sourceLocation) $($_.after)/$($_.required))" }
    }
    if ($r.issues.Count -gt 0) { '--- ISSUES ---'; $r.issues | ForEach-Object { "  $_" } }
}
"receipt: $OutFile"
"PASS: $($report.pass)"
exit $(if ($report.pass) { 0 } else { 1 })
