param(
    [string]$Base    = 'http://127.0.0.1:5000',
    [string]$OutFile = 'C:\GSDDashboard_secure\.tmp-shiftplan-roster-20261015\acceptance-local.json',
    [string]$Kid     = 'S69307',
    [string]$EmployeeId = '9128154'
)
# Acceptance for the Shift Plan roster fix (2026-10-15), read-only GETs.
#   1. /health is 200.
#   2. /api/shifts for the current 7-day window returns the employee.
#   3. /api/shifts for the NEXT Monday..Sunday window returns the employee, even though
#      the employee has no ShiftEntries row in it (the placeholder-row rule).
#   4. Every active employee of /api/employees?active=true is present in /api/shifts,
#      i.e. the plan is a roster of people, not of shift rows.
#   5. The employee's own rows are unchanged: 2026-10-15 WIC_DUTY 08:00-17:00 with the
#      Hamburg task, carrying the RTM marker - no row written by this task.
# Never signs in with the RTM KID (S60028).
$ErrorActionPreference = 'Continue'
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }
$today = (Get-Date).Date
$from  = $today.ToString('yyyy-MM-dd')
$to    = $today.AddDays(6).ToString('yyyy-MM-dd')
# Next ISO week (Monday..Sunday after the current one).
$daysToMonday = ((([int][DayOfWeek]::Monday) - ([int]$today.DayOfWeek) + 7) % 7); if ($daysToMonday -eq 0) { $daysToMonday = 7 }
$nextMon = $today.AddDays($daysToMonday)
$nextFrom = $nextMon.ToString('yyyy-MM-dd')
$nextTo   = $nextMon.AddDays(6).ToString('yyyy-MM-dd')

$checks = New-Object System.Collections.ArrayList
function Add-Result([string]$name, [bool]$ok, $detail) {
    [void]$checks.Add([pscustomobject]@{ check = $name; ok = $ok; detail = $detail })
    Write-Output ("{0,-5} {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

$s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$me = $null
try {
    $me = Invoke-RestMethod -Uri "$Base/api/auth/login" -Method Post -ContentType 'application/json' `
        -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $s -Headers $H -TimeoutSec 30
} catch { }
Add-Result 'login' ($null -ne $me) $(if ($me) { "$($me.role)" } else { 'login failed' })

try {
    $r = Invoke-WebRequest -Uri "$Base/health" -Headers $H -UseBasicParsing -TimeoutSec 30 -SkipHttpErrorCheck
    Add-Result 'health-200' ([int]$r.StatusCode -eq 200) ([int]$r.StatusCode)
} catch { Add-Result 'health-200' $false $_.Exception.Message }

function GetJson([string]$route) {
    $r = Invoke-WebRequest -Uri "$Base$route" -WebSession $s -Headers $H -UseBasicParsing -TimeoutSec 120 -SkipHttpErrorCheck
    if ([int]$r.StatusCode -ne 200) { return $null }
    return ($r.Content | ConvertFrom-Json)
}

# 1 + 2: current window
$cur = GetJson "/api/shifts?from=$from&to=$to"
$curIds = @($cur | Select-Object -ExpandProperty employeeId -Unique)
$empRows = @($cur | Where-Object { $_.employeeId -eq $EmployeeId })
Add-Result "current-window($from..$to)-returns-$EmployeeId" ($empRows.Count -gt 0) "rows=$($empRows.Count) windowEmployees=$($curIds.Count)"

# 3: next window (no ShiftEntries row for the employee expected)
$nxt = GetJson "/api/shifts?from=$nextFrom&to=$nextTo"
$nxtIds = @($nxt | Select-Object -ExpandProperty employeeId -Unique)
$nxtEmp = @($nxt | Where-Object { $_.employeeId -eq $EmployeeId })
Add-Result "next-window($nextFrom..$nextTo)-returns-$EmployeeId" ($nxtEmp.Count -gt 0) "rows=$($nxtEmp.Count) windowEmployees=$($nxtIds.Count) types=$((@($nxtEmp | Select-Object -ExpandProperty shiftType -Unique)) -join ',')"

# 4: the plan covers every active employee
$emps = GetJson '/api/employees?active=true'
$activeIds = @($emps | Where-Object { $_.isActive } | Select-Object -ExpandProperty employeeId -Unique)
$missing = @($activeIds | Where-Object { $curIds -notcontains $_ })
Add-Result 'plan-covers-every-active-employee' ($missing.Count -eq 0) "active=$($activeIds.Count) inPlan=$($curIds.Count) missing=$($missing -join ',')"

# 5: the employee's own rows are untouched (RTM-written, not this task)
$own = @($cur | Where-Object { $_.employeeId -eq $EmployeeId -and $_.shiftDate -eq '2026-10-15' })
if ($own.Count -eq 1) {
    $o = $own[0]
    Add-Result 'own-row-2026-10-15-unchanged' ($o.shiftType -eq 'WIC_DUTY' -and $o.agentTask -eq 'Hamburg' -and $o.shiftStart -eq '08:00' -and $o.shiftEnd -eq '17:00' -and $o.id -eq 71365) "id=$($o.id) $($o.shiftType) $($o.shiftStart)-$($o.shiftEnd) task=$($o.agentTask)"
} else {
    Add-Result 'own-row-2026-10-15-unchanged' $false "rows=$($own.Count)"
}

# The employee list itself (Employees) must still contain the row.
Add-Result 'employees-list-contains-employee' ($activeIds -contains $EmployeeId) "active=$($activeIds.Count)"

$failed = @($checks | Where-Object { -not $_.ok })
[pscustomobject]@{
    when  = (Get-Date).ToString('o')
    base  = $Base
    kid   = $Kid
    employeeId = $EmployeeId
    windows = @{ current = "$from..$to"; next = "$nextFrom..$nextTo" }
    passed = @($checks | Where-Object { $_.ok }).Count
    failed = $failed.Count
    checks = $checks
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutFile -Encoding UTF8

"== $Base =="
"passed $($checks.Count - $failed.Count)/$($checks.Count); receipt: $OutFile"
exit $(if ($failed.Count -gt 0) { 1 } else { 0 })

