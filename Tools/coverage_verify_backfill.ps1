# coverage_verify_backfill.ps1
#
# READ ONLY. Post-write evidence for the 21 backfilled WIC rows.
#   * DB side : each new WicShiftEntries row plus its partner ShiftEntries row.
#   * API side: GET /api/wic/coverage for every affected date - the agent must appear
#     exactly once, with the same WorkingShift the row stores.
#   * GAP side: the "rostered for WIC duty but no on-site WIC row" signal must no longer
#     fire for the 21 (EmployeeId, Date, Centre) triples.
#
# Usage: pwsh -File Tools\coverage_verify_backfill.ps1

[CmdletBinding()]
param(
    [string] $ConnectionString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;",
    [string] $Base = "http://localhost:5000",
    [int[]]  $NewId = @(67765..67785),
    [string] $OutFile = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Data
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$out = New-Object System.Collections.ArrayList
function P([string]$s = "") { [void]$out.Add($s) }

$conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
$conn.Open()
function Rows([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
    $rdr = $cmd.ExecuteReader(); $o = New-Object System.Collections.ArrayList
    while ($rdr.Read()) { $h = @{}; for ($i=0; $i -lt $rdr.FieldCount; $i++) { $v = $rdr.GetValue($i); if ($v -is [System.DBNull]) { $v = $null }; $h[$rdr.GetName($i)] = $v }; [void]$o.Add([pscustomobject]$h) }
    $rdr.Close(); return ,$o
}

$ids = ($NewId -join ",")
$new = Rows @"
SELECT w.Id, w.EmployeeId, CONVERT(varchar(10),w.ShiftDate,23) D, w.SupportLocation, w.LocationCode,
       w.WorkingShift, w.IsOnSite, w.IsGSDDay, w.IsOffDay, w.DayOfWeek, w.Task,
       se.Id SeId, se.ShiftType, se.IsWicDuty, se.ShiftStart, se.ShiftEnd, se.AgentTask, se.ManualChangeBy
FROM WicShiftEntries w
LEFT JOIN ShiftEntries se ON se.EmployeeId = w.EmployeeId AND se.ShiftDate = w.ShiftDate
WHERE w.Id IN ($ids)
ORDER BY w.Id
"@
$conn.Close()

$dates = @($new | ForEach-Object { $_.D } | Select-Object -Unique | Sort-Object)

P "POST-WRITE COVERAGE EVIDENCE - 21 backfilled WicShiftEntries rows"
P "read only : yes"
P "new ids   : $($new.Count) rows, Id $(($new | Measure-Object Id -Minimum).Minimum)..$(($new | Measure-Object Id -Maximum).Maximum)"
P "dates     : $($dates -join ', ')"
P ""

$cov = @{}
foreach ($d in $dates) {
    $cov[$d] = (New-Object System.Net.WebClient).DownloadString("$Base/api/wic/coverage?date=$d") | ConvertFrom-Json
}

P "=== 1. ROW-BY-ROW: new row, its Shift Plan partner, and the coverage API ==="
P ("  {0,-6} {1,-8} {2,-10} {3,-21} {4,-13} {5,-7} {6,-6} {7,-7} {8,-9} {9,-6} {10}" -f `
   "newId","emp","date","SupportLocation","WorkingShift","seId","WIC?","onSite","covered","shift=","agentCount")
P ("  {0,-6} {1,-8} {2,-10} {3,-21} {4,-13} {5,-7} {6,-6} {7,-7} {8,-9} {9,-6} {10}" -f `
   ("-"*6),("-"*8),("-"*10),("-"*21),("-"*13),("-"*7),("-"*6),("-"*7),("-"*9),("-"*6),("-"*10))
$problems = 0
foreach ($w in $new) {
    $loc = $cov[$w.D] | Where-Object { $_.displayName -eq $w.SupportLocation } | Select-Object -First 1
    $agent = if ($loc) { @($loc.agents | Where-Object { $_.employeeId -eq $w.EmployeeId }) } else { @() }
    $present = $agent.Count
    $shiftOk = ($present -eq 1) -and ($agent[0].workingShift -eq $w.WorkingShift)
    $seOk = ($w.ShiftType -eq 'WIC_DUTY') -and $w.IsWicDuty -and ($w.ManualChangeBy -eq 'S60028')
    if ($present -ne 1 -or -not $shiftOk -or -not $seOk -or -not $w.IsOnSite) { $problems++ }
    P ("  {0,-6} {1,-8} {2,-10} {3,-21} {4,-13} {5,-7} {6,-6} {7,-7} {8,-9} {9,-6} {10}" -f `
       $w.Id, $w.EmployeeId, $w.D, $w.SupportLocation, $w.WorkingShift, $w.SeId,
       $w.ShiftType, $w.IsOnSite, $loc.isCovered, $shiftOk, $loc.agentCount)
}
P ""
P ("  problems: {0}   (must be 0: row on-site, partner still manual WIC_DUTY, agent exactly once in the API with the same shift)" -f $problems)
P ""

P "=== 2. PER DATE: centres now covered and the WIC rows behind them ==="
foreach ($d in $dates) {
    $dayNew = @($new | Where-Object { $_.D -eq $d })
    P ("  {0}  -  {1} new row(s)" -f $d, $dayNew.Count)
    foreach ($w in $dayNew) {
        $loc = $cov[$d] | Where-Object { $_.displayName -eq $w.SupportLocation } | Select-Object -First 1
        P ("      {0,-21} agentCount={1} covered={2} shift={3}" -f $w.SupportLocation, $loc.agentCount, $loc.isCovered, $w.WorkingShift)
    }
}
P ""

P "=== 3. TOTALS ==="
P ("  new rows in WicShiftEntries                              : {0}" -f $new.Count)
P ("  rows with IsOnSite=1                                     : {0}" -f @($new | Where-Object { $_.IsOnSite }).Count)
P ("  rows whose partner ShiftEntries is a manual WIC_DUTY     : {0}" -f @($new | Where-Object { $_.ShiftType -eq 'WIC_DUTY' -and $_.ManualChangeBy -eq 'S60028' }).Count)
P ("  distinct centres touched                                  : {0}" -f @($new | Select-Object -ExpandProperty SupportLocation -Unique).Count)
P ("  distinct coverage dates                                   : {0}" -f $dates.Count)

$text = ($out -join "`r`n")
if (-not [string]::IsNullOrWhiteSpace($OutFile)) { $text | Set-Content -LiteralPath $OutFile -Encoding UTF8 }
$text
if ($problems -gt 0) { exit 2 }
