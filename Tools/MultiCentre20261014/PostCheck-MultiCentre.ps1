# PostCheck-MultiCentre.ps1  --  item 7 of the task, READ ONLY (GET only).
#   1. /health == 200 on localhost and on the real devtunnel;
#   2. GET /api/wic/conflicts?from=2026-10-14&to=2026-10-30 -> 0 conflicts;
#   3. GET /api/hr-export?from=2026-10-15&to=2026-10-15&employeeId=9120980&types=ShiftPlan&format=csv
#      -> the ShiftPlan row for 2026-10-15 carries ShiftStart 10:00 / ShiftEnd 15:00.
# Sign-in uses the DEV operator KID S69307, never the RTM KID S60028 (no RTM session anywhere).
param(
    [string]$LocalBase  = 'http://127.0.0.1:5000',
    [string]$TunnelBase = 'https://d2jn94qg-5000.euw.devtunnels.ms',
    [string]$Kid        = 'S69307',
    [string]$EmployeeId = '9120980',
    [string]$Date       = '2026-10-15',
    [string]$ConflictFrom = '2026-10-14',
    [string]$ConflictTo   = '2026-10-30',
    [string]$OutDir     = 'C:\GSDDashboard_secure\.tmp-multicentre-deploy'
)
$ErrorActionPreference = 'Continue'
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$hdrs = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }
$report = [ordered]@{ when = (Get-Date).ToString('o'); checks = @() }
function Add-Check([string]$name, [bool]$ok, $detail) {
    $script:report.checks += [pscustomobject]@{ name = $name; ok = $ok; detail = $detail }
    Write-Output ("{0,-58} {1}  {2}" -f $name, $(if ($ok) { 'OK  ' } else { 'FAIL' }), $detail)
}

foreach ($base in @($LocalBase, $TunnelBase)) {
    $s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $label = if ($base -eq $LocalBase) { 'local' } else { 'tunnel' }

    try {
        $login = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType 'application/json' `
            -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $s -Headers $hdrs -TimeoutSec 30
        Add-Check "$label login ($Kid)" $true "role=$($login.role)"
    } catch { Add-Check "$label login ($Kid)" $false $_.Exception.Message; continue }

    try {
        $health = Invoke-WebRequest -Uri "$base/health" -WebSession $s -Headers $hdrs -UseBasicParsing -TimeoutSec 30
        Add-Check "$label /health" ($health.StatusCode -eq 200) "status=$($health.StatusCode) body=$($health.Content)"
    } catch { Add-Check "$label /health" $false $_.Exception.Message }

    try {
        $c = Invoke-RestMethod -Uri "$base/api/wic/conflicts?from=$ConflictFrom&to=$ConflictTo" -WebSession $s -Headers $hdrs -TimeoutSec 120
        $n = @($c).Count
        Add-Check "$label /api/wic/conflicts $ConflictFrom..$ConflictTo" ($n -eq 0) "conflicts=$n"
        if ($n -gt 0) { $c | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir "conflicts-$label.json") -Encoding UTF8 }
    } catch { Add-Check "$label /api/wic/conflicts" $false $_.Exception.Message }

    # HR export (CSV zip) for the one employee and the one day.
    try {
        $zipPath = Join-Path $OutDir "hr-export-$label.zip"
        Invoke-WebRequest -Uri "$base/api/hr-export?from=$Date&to=$Date&employeeId=$EmployeeId&types=ShiftPlan&format=csv" `
            -WebSession $s -Headers $hdrs -UseBasicParsing -TimeoutSec 180 -OutFile $zipPath
        $extract = Join-Path $OutDir "hr-export-$label"
        if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extract -Force
        $csv = Join-Path $extract 'ShiftPlan.csv'
        if (-not (Test-Path -LiteralPath $csv)) { throw "ShiftPlan.csv missing in $zipPath" }
        $rows = Import-Csv -LiteralPath $csv
        $row = @($rows | Where-Object { $_.EmployeeId -eq $EmployeeId -and $_.Date -eq $Date })
        if ($row.Count -ne 1) { throw "expected exactly one ShiftPlan row for $EmployeeId on $Date, found $($row.Count)" }
        $r = $row[0]
        Copy-Item -LiteralPath $csv -Destination (Join-Path $OutDir "hr-export-$label-ShiftPlan.csv") -Force
        $ok = ($r.ShiftStart -eq '10:00' -and $r.ShiftEnd -eq '15:00')
        Add-Check "$label HR export $EmployeeId $Date ShiftStart/ShiftEnd" $ok "ShiftType=$($r.ShiftType) start=$($r.ShiftStart) end=$($r.ShiftEnd) task=$($r.AgentTask)"
    } catch { Add-Check "$label HR export" $false $_.Exception.Message }
}

$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'postcheck.json') -Encoding UTF8
$failed = @($report.checks | Where-Object { -not $_.ok })
Write-Output ''
Write-Output ("POSTCHECK: {0}/{1} OK" -f (@($report.checks).Count - $failed.Count), @($report.checks).Count)
exit $(if ($failed.Count -gt 0) { 1 } else { 0 })

