# Recon-MultiCentre.ps1  --  READ ONLY
# Item 2 of the task: every day from 2026-10-14 onward on which one employee has
# 2+ IsOnSite=1 WicShiftEntries rows at different centres, where the daily ShiftEntries
# row does NOT cover earliest WIC start .. latest WIC end.
#
# Writes only CSV/txt evidence under .tmp-multicentre-20261014. Issues SELECTs only.
param(
    [string]$OutDir = 'C:\GSDDashboard_secure\.tmp-multicentre-20261014',
    [string]$From   = '2026-10-14'
)
$ErrorActionPreference = 'Stop'
. 'C:\GSDDashboard_secure\Tools\MultiCentre20261014\MultiCentre.Common.ps1'

if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

$conn = Get-RtmConnection
try {
    $a = Get-McAnalysis $conn ([datetime]$From)

    $all = @($a.Results)
    $viol = @($all | Where-Object { $_.SpanMissing })

    Write-Output ("WicShiftEntries IsOnSite rows >= {0}: {1}" -f $From, $a.WicCount)
    Write-Output ("ShiftEntries rows >= {0}: {1}" -f $From, $a.ShiftCount)
    Write-Output ("Employee/date groups with 2+ centres: {0}" -f $all.Count)
    Write-Output ("  of those, ShiftEntries does NOT span all WIC parts: {0}" -f $viol.Count)
    Write-Output ("Unresolved SupportLocation rows: {0}" -f @($a.Unresolved).Count)

    $all  | Export-Csv -LiteralPath (Join-Path $OutDir 'item2-all-multicentre.csv')  -NoTypeInformation -Encoding UTF8
    $viol | Export-Csv -LiteralPath (Join-Path $OutDir 'item2-violations.csv')      -NoTypeInformation -Encoding UTF8
    if (@($a.Unresolved).Count -gt 0) {
        $a.Unresolved | Export-Csv -LiteralPath (Join-Path $OutDir 'item2-unresolved.csv') -NoTypeInformation -Encoding UTF8
    }

    Write-Output ''
    Write-Output '--- VIOLATIONS (ShiftEntries does not span all WIC parts) ---'
    $viol | Select-Object EmployeeId, IsActive, ShiftDate, DOW, CentreCount, Centres, ShiftEntryId, ShiftType,
                           ManualChangeBy, OldShift, NewShift, WicFixCount, WicFixDetail |
        Format-Table -AutoSize | Out-String -Width 400 | Write-Output

    Write-Output ''
    Write-Output '--- all multi-centre days: span status ---'
    $all | Select-Object EmployeeId, ShiftDate, CentreCount, Centres, OldShift, NewShift, SpanMissing, WicFixCount |
        Format-Table -AutoSize | Out-String -Width 400 | Write-Output
}
finally { $conn.Close() }
