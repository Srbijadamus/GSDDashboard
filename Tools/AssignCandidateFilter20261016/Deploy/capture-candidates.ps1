param(
    [string]$Base   = 'http://127.0.0.1:5000',
    [string]$Tunnel = 'https://d2jn94qg-5000.euw.devtunnels.ms',
    [string]$Kid    = 'S69307',
    [string]$OutFile,
    [switch]$TunnelToo
)
# READ-ONLY measurement of the Assign-Agent candidate list (GET only; no writes anywhere).
#
# For every (location, date) below it records:
#   * the candidate list exactly as the running backend returns it,
#   * the candidate list the NEW shared rule would return (computed from Employers/
#     ShiftEntries: drop IsActive=0 and anyone with a RESIGNED ShiftEntries row on/before
#     the day - the same SubstituteEligibility.HasLeft the code applies),
#   * the AgentTask rows of the named control employee/days (unchanged by this task).
$ErrorActionPreference = 'Stop'
$H = @{ 'X-Tunnel-Skip-AntiPhishing-Page' = 'true' }

$targets = @(
    @{ Name = 'Grafenrheinfeld'; Code = 'DE~97506~Grafenrheinfeld~Kraftwerkstraße'; Date = '2026-10-16' },
    @{ Name = 'Regensburg';      Code = 'DE~93049~Regensburg~Lilienthalstraße 7';   Date = '2026-10-20' },
    @{ Name = 'Helmstedt';       Code = 'DE~38350~Helmstedt~Schillerstr. 3';        Date = '2026-10-16' },
    @{ Name = 'Pfaffenhofen';    Code = 'DE~85276~Pfaffenhofen~Draht 7';            Date = '2026-10-16' },
    @{ Name = 'Pfaffenhofen';    Code = 'DE~85276~Pfaffenhofen~Draht 7';            Date = '2026-10-23' }
)

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$login = Invoke-RestMethod -Uri "$Base/api/auth/login" -Method Post -ContentType 'application/json' `
    -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $session -Headers $H -TimeoutSec 30

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=true;"
Add-Type -AssemblyName System.Data
$conn = New-Object System.Data.SqlClient.SqlConnection($cs); $conn.Open()
function Q([string]$sql, $pars = $null) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
    if ($pars) { foreach ($pv in $pars.GetEnumerator()) { $p = $cmd.CreateParameter(); $p.ParameterName = $pv.Key; $p.Value = $pv.Value; [void]$cmd.Parameters.Add($p) } }
    $rdr = $cmd.ExecuteReader(); $out = New-Object System.Collections.ArrayList
    while ($rdr.Read()) { $h = [ordered]@{}; for ($i = 0; $i -lt $rdr.FieldCount; $i++) { $v = $rdr.GetValue($i); if ($v -is [System.DBNull]) { $v = $null }; $h[$rdr.GetName($i)] = $v }; [void]$out.Add([pscustomobject]$h) }
    $rdr.Close(); return ,$out
}

$results = @()
foreach ($t in $targets) {
    $day = $t.Date
    $enc = [uri]::EscapeDataString($t.Code)
    $live = Invoke-RestMethod -Uri "$Base/api/wic/assign-candidates?locationCode=$enc&date=$day" `
        -WebSession $session -Headers $H -TimeoutSec 120
    $liveIds = @($live.candidates | ForEach-Object { $_.employeeId })

    # IsActive=0 rows and RESIGNED-on/before-the-day rows: the two things the shared rule drops.
    $inactive = @(Q "SELECT EmployeeId FROM Employees WHERE IsActive=0")
    $inactiveIds = @($inactive | ForEach-Object { $_.EmployeeId })
    $resigned = @(Q @"
SELECT DISTINCT EmployeeId FROM ShiftEntries
WHERE ShiftType='RESIGNED' AND ShiftDate <= CONVERT(date, @d, 23)
"@ @{ '@d' = $day })
    $resignedIds = @($resigned | ForEach-Object { $_.EmployeeId })

    $expectedIds   = @($liveIds | Where-Object { $inactiveIds -notcontains $_ -and $resignedIds -notcontains $_ })
    $removedIds    = @($liveIds | Where-Object { $inactiveIds -contains $_ -or $resignedIds -contains $_ })
    $removedDetail = @($removedIds | ForEach-Object {
        $id = $_
        [pscustomobject]@{
            EmployeeId = $id
            Reason = if ($inactiveIds -contains $id -and $resignedIds -contains $id) { 'INACTIVE_AND_RESIGNED' }
                     elseif ($inactiveIds -contains $id) { 'INACTIVE' } else { 'RESIGNED' }
        }
    })

    $results += [pscustomobject]@{
        location = $t.Name; locationCode = $t.Code; date = $day
        liveCandidates   = $liveIds.Count
        expectedAfter    = $expectedIds.Count
        removed          = $removedIds.Count
        removedIds       = $removedDetail
        hasSensible      = $live.hasSensibleCandidate
        onDemand9128154Offered = ($liveIds -contains '9128154')
    }
}

# Control employee/days: the shift rows the task says must not move.
$control = Q @"
SELECT Id, EmployeeId, CONVERT(varchar(10),ShiftDate,23) D, ShiftType, ISNULL(RawValue,'') RawValue,
       ISNULL(AgentTask,'') AgentTask, ISNULL(ShiftStart,'') S, ISNULL(ShiftEnd,'') E,
       SourceModule, CASE WHEN ManualChangeBy IS NULL THEN '' ELSE ManualChangeBy END ManualBy
FROM ShiftEntries
WHERE (EmployeeId IN ('9129428','9135516','9129429','9130649','9130657'))
  AND (ShiftDate='2026-10-16' OR ShiftDate='2026-10-20' OR ShiftDate='2026-10-23')
ORDER BY EmployeeId, ShiftDate, Id
"@

$receipt = [pscustomobject]@{
    when = (Get-Date).ToString('o')
    base = $Base
    loginKid = $Kid
    loginRole = $login.role
    results = $results
    controlShiftRows = $control
}
if ($TunnelToo) {
    $ts = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    Invoke-RestMethod -Uri "$Tunnel/api/auth/login" -Method Post -ContentType 'application/json' `
        -Body (@{ kid = $Kid } | ConvertTo-Json) -WebSession $ts -Headers $H -TimeoutSec 30 | Out-Null
    $tun = @()
    foreach ($t in $targets) {
        $enc = [uri]::EscapeDataString($t.Code)
        $live = Invoke-RestMethod -Uri "$Tunnel/api/wic/assign-candidates?locationCode=$enc&date=$($t.Date)" `
            -WebSession $ts -Headers $H -TimeoutSec 120
        $tun += [pscustomobject]@{ location = $t.Name; date = $t.Date; liveCandidates = @($live.candidates).Count }
    }
    $receipt | Add-Member -NotePropertyName tunnel -NotePropertyValue $tun
}
$conn.Close()

if ($OutFile) { $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutFile -Encoding UTF8; "receipt: $OutFile" }
"login: $($login.role)   base: $Base"
$results | Select-Object location, date, liveCandidates, expectedAfter, removed, onDemand9128154Offered |
    Format-Table -AutoSize | Out-String -Width 200 | Write-Output
"control ShiftEntries rows: $($control.Count)"
$receipt | ConvertTo-Json -Depth 8
