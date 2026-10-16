param(
    [string]$LocalBase  = 'http://127.0.0.1:5000',
    [string]$TunnelBase = 'https://d2jn94qg-5000.euw.devtunnels.ms',
    [string]$OutFile    = 'C:\GSDDashboard_secure\Tools\ShiftPlanLeaverWindow20261016\Deploy\leaver-window-result.json',
    [string]$Kid        = 'S69307',
    [string]$EmployeeId = '9106138'
)
# READ-ONLY acceptance of the Shift Plan leaver-window rule on the live data. It only logs in
# with the DEV operator KID (never the RTM KID S60028) and issues GETs; it writes no row.
#
# The live shape (read-only, verified before deploy): employee 9106138 is ACTIVE and carries
# RESIGNED ShiftEntries from 2026-10-26 (and later 10-29, 11-02, 11-03). Therefore:
#   * 2026-10-20..2026-10-26 — the exit is INSIDE the window  -> the plan MUST contain him
#     (his OFF/AL/OFF_WEEKEND days and the RESIGNED row on 10-26),
#   * a window that STARTS on/after 2026-10-27 — he had left before it -> MUST NOT contain him.
$ErrorActionPreference = 'Continue'
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }

$checks = @(
    @{ name = 'inside-2026-10-20..26'; from = '2026-10-20'; to = '2026-10-26'; expect = $true  },
    @{ name = 'after-2026-10-27..11-02'; from = '2026-10-27'; to = '2026-11-02'; expect = $false },
    @{ name = 'from-2026-10-27-single'; from = '2026-10-27'; to = '2026-10-27'; expect = $false },
    @{ name = 'before-2026-10-19..25'; from = '2026-10-19'; to = '2026-10-26'; expect = $true  }
)

$result = [ordered]@{}
$allOk = $true
foreach ($base in @($LocalBase, $TunnelBase)) {
    $s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $login = -1; $role = ''
    try {
        $l = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType 'application/json' `
            -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $s -Headers $H -TimeoutSec 30
        $login = 200; $role = $l.role
    } catch { $role = "login-failed: $($_.Exception.Message)" }

    $health = -1
    try { $health = [int](Invoke-WebRequest -Uri "$base/health" -WebSession $s -Headers $H -UseBasicParsing -TimeoutSec 30 -SkipHttpErrorCheck).StatusCode } catch { }

    $rows = @()
    foreach ($c in $checks) {
        $status = -1; $contains = $null; $count = -1; $resignedDates = @()
        try {
            $r = Invoke-WebRequest -Uri "$base/api/shifts?from=$($c.from)&to=$($c.to)" -WebSession $s -Headers $H -UseBasicParsing -TimeoutSec 120 -SkipHttpErrorCheck
            $status = [int]$r.StatusCode
            $j = [string]$r.Content | ConvertFrom-Json
            $mine = @($j | Where-Object { $_.employeeId -eq $EmployeeId })
            $ids = @($j | ForEach-Object { $_.employeeId })
            $count = $ids.Count
            $contains = ($ids -contains $EmployeeId)
            $resignedDates = @($mine | Where-Object { $_.shiftType -eq 'RESIGNED' } | ForEach-Object { $_.shiftDate })
        } catch { }
        $ok = ($status -eq 200 -and $contains -eq $c.expect)
        if (!$ok) { $allOk = $false }
        $rows += [pscustomobject]@{
            name = $c.name; from = $c.from; to = $c.to; status = $status
            totalRows = $count; contains = $contains; expected = $c.expect
            resignedDates = ($resignedDates -join ','); ok = $ok
        }
    }
    $result[$base] = [ordered]@{ login = [ordered]@{ status = $login; role = $role }; health = $health; checks = $rows }
    "== $base =="
    "login=$login/$role  health=$health"
    $rows | Format-Table -AutoSize | Out-String -Width 220 | Write-Output
}

$out = [ordered]@{
    when = (Get-Date).ToString('o')
    employeeId = $EmployeeId
    pass = $allOk
    bases = $result
}
$out | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $OutFile -Encoding UTF8
"PASS=$allOk  receipt: $OutFile"
exit $(if ($allOk) { 0 } else { 1 })
