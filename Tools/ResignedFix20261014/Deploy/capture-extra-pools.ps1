param(
    [string]$Base = 'http://127.0.0.1:5000',
    [string]$OutFile = 'C:\GSDDashboard_secure\.tmp-resigned-deploy\after-extra-pools.json'
)
# READ-ONLY audit of the OTHER candidate pools that were not in the four changed sources,
# over exactly the location/days the coverage-gaps endpoint reports as gaps:
#   /api/wic/backup        — BackupService backup candidates
#   /api/vwic/candidates   — VWIC rotation pool
# and /api/employees/?active=false for the inactive set. Counts only ids that are inactive
# or carry a RESIGNED ShiftEntries row on/before the day.
$ErrorActionPreference = 'Stop'
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }
$s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-RestMethod -Uri "$Base/api/auth/login" -Method Post -ContentType 'application/json' `
    -Body (@{ kid = 'S69307' } | ConvertTo-Json) -WebSession $s -Headers $H -TimeoutSec 30 | Out-Null

$employees = Invoke-RestMethod -Uri "$Base/api/employees/?active=false" -WebSession $s -Headers $H -TimeoutSec 120
$inactive = @($employees | Where-Object { -not $_.isActive } | ForEach-Object { [string]$_.employeeId })

$resignedFrom = @{}
$cn = New-Object System.Data.SqlClient.SqlConnection "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=true;"
$cn.Open(); $cmd = $cn.CreateCommand()
$cmd.CommandText = "SELECT EmployeeId, MIN(ShiftDate) AS FirstResigned FROM ShiftEntries WHERE ShiftType = 'RESIGNED' GROUP BY EmployeeId"
$rd = $cmd.ExecuteReader()
while ($rd.Read()) { $resignedFrom[[string]$rd['EmployeeId']] = ([datetime]$rd['FirstResigned']).ToString('yyyy-MM-dd') }
$rd.Close(); $cn.Close()

function Bad([string]$id, [string]$date) {
    $r = @()
    if ($id -and ($inactive -contains $id)) { $r += 'INACTIVE' }
    if ($id -and $resignedFrom.ContainsKey($id) -and $resignedFrom[$id] -le $date) { $r += "RESIGNED_SINCE_$($resignedFrom[$id])" }
    return $r
}

$gaps = Invoke-RestMethod -Uri "$Base/api/wic/coverage-gaps?days=28" -WebSession $s -Headers $H -TimeoutSec 180
$badBackup = @(); $badVwic = @(); $probed = 0
foreach ($g in $gaps.gaps) {
    $enc = [uri]::EscapeDataString($g.locationCode)
    $probed++
    try {
        $b = Invoke-RestMethod -Uri "$Base/api/wic/backup?locationCode=$enc&date=$($g.date)&horizon=1" -WebSession $s -Headers $H -TimeoutSec 120
        foreach ($d in $b.days) { foreach ($c in $d.candidates) { $r = Bad $c.employeeId $d.date; if ($r.Count) { $badBackup += [pscustomobject]@{ date=$d.date; locationCode=$d.locationCode; employeeId=$c.employeeId; source=$c.source; reasons=($r -join ',') } } } }
    } catch { }
}
$v = Invoke-RestMethod -Uri "$Base/api/vwic/candidates" -WebSession $s -Headers $H -TimeoutSec 120
foreach ($c in $v) { $r = Bad $c.employeeId ([datetime]::Today.ToString('yyyy-MM-dd')); if ($r.Count) { $badVwic += [pscustomobject]@{ employeeId=$c.employeeId; reasons=($r -join ',') } } }

$result = [pscustomobject]@{
    when = (Get-Date).ToString('o'); base = $Base
    probedLocationDays = $probed
    backupCandidates = $badBackup
    vwicCandidates = $badVwic
    inactiveIds = $inactive
    resignedIds = @($resignedFrom.Keys)
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutFile -Encoding UTF8
"probed location/days : $probed"
"bad backup candidates: $($badBackup.Count)"
"bad vwic candidates  : $($badVwic.Count)"
"receipt: $OutFile"
