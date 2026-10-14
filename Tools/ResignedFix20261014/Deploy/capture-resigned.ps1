param(
    [string]$Base   = 'http://127.0.0.1:5000',
    [string]$OutFile = 'C:\GSDDashboard_secure\.tmp-resigned-deploy\baseline-before.json'
)
# READ-ONLY capture: which coverage-gaps / briefing suggestions name an employee who is
# inactive (IsActive=0) or has a RESIGNED ShiftEntries row on/before the suggested date.
# No writes anywhere: GET requests + SELECT only.
$ErrorActionPreference = 'Stop'
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true'; 'X-Pulse-Request' = '1' }

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$login = Invoke-RestMethod -Uri "$Base/api/auth/login" -Method Post -ContentType 'application/json' `
    -Body (@{ kid = 'S69307' } | ConvertTo-Json) -WebSession $session -Headers $H -TimeoutSec 30
Write-Output "login: $($login.role)"

$employees = Invoke-RestMethod -Uri "$Base/api/employees/?active=false" -WebSession $session -Headers $H -TimeoutSec 120
$empById = @{}
foreach ($e in $employees) { $empById[[string]$e.employeeId] = $e }
Write-Output "employees: $($employees.Count)"

# Read-only: earliest RESIGNED ShiftEntries date per employee (live GSDDashboard).
$resignedFrom = @{}
$cn = New-Object System.Data.SqlClient.SqlConnection "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=true;"
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandText = "SELECT EmployeeId, MIN(ShiftDate) AS FirstResigned FROM ShiftEntries WHERE ShiftType = 'RESIGNED' GROUP BY EmployeeId"
$rd = $cmd.ExecuteReader()
while ($rd.Read()) {
    $d = [datetime]$rd['FirstResigned']
    $resignedFrom[[string]$rd['EmployeeId']] = $d.ToString('yyyy-MM-dd')
}
$rd.Close(); $cn.Close()
Write-Output "employees with a RESIGNED ShiftEntries row: $($resignedFrom.Count)"

function Test-Bad([string]$empId, [string]$date, [string]$status) {
    $reasons = @()
    if ($empId -and $empById.ContainsKey($empId) -and -not $empById[$empId].isActive) { $reasons += 'INACTIVE' }
    if ($status -eq 'RESIGNED') { $reasons += 'RESIGNED_STATUS' }
    if ($empId -and $resignedFrom.ContainsKey($empId) -and $resignedFrom[$empId] -le $date) { $reasons += "RESIGNED_SINCE_$($resignedFrom[$empId])" }
    return $reasons
}

$gaps = Invoke-RestMethod -Uri "$Base/api/wic/coverage-gaps?days=28" -WebSession $session -Headers $H -TimeoutSec 180
Write-Output ("gaps: {0} (critical {1}, warning {2})" -f $gaps.gaps.Count, $gaps.criticalCount, $gaps.warningCount)

$badAssigned = @()
$badFloaters = @()
foreach ($g in $gaps.gaps) {
    foreach ($a in $g.assignedAgents) {
        $r = Test-Bad $a.employeeId $g.date $a.status
        if ($r.Count) { $badAssigned += [pscustomobject]@{ date=$g.date; locationCode=$g.locationCode; displayName=$g.displayName; list='assignedAgents'; employeeId=$a.employeeId; status=$a.status; reasons=($r -join ',') } }
    }
    foreach ($a in $g.availableFloaters) {
        $r = Test-Bad $a.employeeId $g.date $a.status
        if ($r.Count) { $badFloaters += [pscustomobject]@{ date=$g.date; locationCode=$g.locationCode; displayName=$g.displayName; list='availableFloaters'; employeeId=$a.employeeId; status=$a.status; reasons=($r -join ',') } }
    }
}
Write-Output "bad availableFloaters: $($badFloaters.Count)"
Write-Output "bad assignedAgents  : $($badAssigned.Count)"

# ── Briefing "Gaps & recommendations" (Best substitute) ──────────────────────
$badBest = @()
try {
    $briefing = Invoke-RestMethod -Uri "$Base/api/wic/briefing" -WebSession $session -Headers $H -TimeoutSec 180
    $today = ([datetime]$briefing.date).ToString('yyyy-MM-dd')
    foreach ($g in $briefing.gaps) {
        $r = Test-Bad $g.bestSubstituteId $today $null
        if ($r.Count) { $badBest += [pscustomobject]@{ locationCode=$g.locationCode; displayName=$g.displayName; bestSubstituteId=$g.bestSubstituteId; bestSubstituteName=$g.bestSubstituteName; bestSubstituteSource=$g.bestSubstituteSource; reasons=($r -join ',') } }
    }
    Write-Output "bad briefing bestSubstitute: $($badBest.Count)  (briefing gaps: $($briefing.gaps.Count))"
} catch { Write-Output "briefing: $($_.Exception.Message)"; $briefing = $null }

# ── Find Substitute (SubstitutionService) for every gap location/date ────────
$badSubs = @()
$probed = @{}
foreach ($g in $gaps.gaps) {
    $key = "$($g.locationCode)|$($g.date)"
    if ($probed.ContainsKey($key)) { continue }
    $probed[$key] = $true
    try {
        $enc = [uri]::EscapeDataString($g.locationCode)
        $sub = Invoke-RestMethod -Uri "$Base/api/wic/substitutes?locationCode=$enc&date=$($g.date)&horizon=1" -WebSession $session -Headers $H -TimeoutSec 120
        foreach ($d in $sub.days) {
            foreach ($c in $d.candidates) {
                $r = Test-Bad $c.employeeId $d.date $null
                if ($r.Count) { $badSubs += [pscustomobject]@{ date=$d.date; locationCode=$d.locationCode; employeeId=$c.employeeId; fullName=$c.fullName; sourceType=$c.sourceType; reasons=($r -join ',') } }
            }
        }
    } catch { }
}
Write-Output "bad substitute candidates: $($badSubs.Count)  (probed $($probed.Count) location/days)"

$result = [pscustomobject]@{
    when = (Get-Date).ToString('o')
    base = $Base
    role = $login.role
    windows = @{ coverageGapsDays = 28; gapRows = $gaps.gaps.Count }
    employeesTotal = $employees.Count
    employeesInactive = @($employees | Where-Object { -not $_.isActive }).Count
    resignedRowsTotal = $resignedFrom.Count
    badAvailableFloaters = $badFloaters
    badAssignedAgents = $badAssigned
    badBriefingBestSubstitute = $badBest
    badSubstituteCandidates = $badSubs
    counts = @{
        availableFloaters = $badFloaters.Count
        assignedAgents    = $badAssigned.Count
        briefingBest      = $badBest.Count
        substituteCands   = $badSubs.Count
    }
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutFile -Encoding UTF8
Write-Output "receipt: $OutFile"
