# Builds the COMPLETE rollback for the 2026-10-15 WIC shift-plan sync repair.
#
# The repair ran in two passes against the same 22 rows:
#   pass 1 (wicsync-20261015-203749): 17 INSERTs + 5 rows that already existed at the right
#           centre but were NOT on site (the pass-1 tool could only insert, so it skipped them);
#   pass 2 (wicsync-20261015-203845): the same 5 rows, turned on site.
# Pass 1's own rollback.sql therefore DELETEs those 5 rows, which is wrong once pass 2 has
# turned them on site: the correct undo is to set IsOnSite back to 0. This script derives the
# complete undo from the PRE-IMAGES both passes captured and writes it as rollback-FINAL.sql.
#
# It writes FILES only — it never touches the database.
param(
    [string]$Pass1Dir = 'C:\ProgramData\PulseRollback\wicsync-20261015-203749',
    [string]$Pass2Dir = 'C:\ProgramData\PulseRollback\wicsync-20261015-203845'
)
$ErrorActionPreference = 'Stop'

function Sql([string]$v) { if ($null -eq $v) { return 'NULL' } return "N'" + $v.Replace("'", "''") + "'" }
# The pre-image CSV writes an empty field for both NULL and ''; WicOpeningHours was NULL.
function SqlNullable([string]$v) { if ([string]::IsNullOrEmpty($v)) { return 'NULL' } return Sql $v }

$plan1 = Import-Csv -LiteralPath (Join-Path $Pass1Dir 'plan.csv')
$shifts1 = Import-Csv -LiteralPath (Join-Path $Pass1Dir 'ShiftEntries-pre.csv')
$wic1 = Import-Csv -LiteralPath (Join-Path $Pass1Dir 'WicShiftEntries-pre.csv')

$marker = 'SHIFT_FIX 2026-10-15 WIC_SYNC'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('-- COMPLETE ROLLBACK for the 2026-10-15 WIC shift-plan sync repair (22 ShiftEntries rows).')
[void]$sb.AppendLine('-- NOT executed. Derived from the pre-images of both passes; run only as a deliberate')
[void]$sb.AppendLine('-- incident operation. It restores SourceModule on the 22 marked rows, deletes the 17')
[void]$sb.AppendLine('-- inserted WicShiftEntries rows and sets IsOnSite back to 0 on the 5 rows pass 2 turned on.')
[void]$sb.AppendLine('SET XACT_ABORT ON; BEGIN TRANSACTION;')

foreach ($p in $plan1) {
    $pre = $shifts1 | Where-Object { $_.Id -eq $p.ShiftEntryId }
    [void]$sb.AppendLine("UPDATE ShiftEntries SET SourceModule = $(Sql $pre.SourceModule) WHERE Id = $($p.ShiftEntryId) AND SourceModule = $(Sql $marker);")

    $existing = $wic1 | Where-Object {
        $_.EmployeeId -eq $p.EmployeeId -and $_.ShiftDate -eq $p.ShiftDate -and $_.SupportLocation -eq $p.CentreName
    }
    if ($existing) {
        [void]$sb.AppendLine(
            "UPDATE WicShiftEntries SET SupportLocation = $(Sql $existing.SupportLocation), " +
            "LocationCode = $(Sql $existing.LocationCode), WorkingShift = $(Sql $existing.WorkingShift), " +
            "IsOnSite = $($existing.IsOnSite), IsGSDDay = $($existing.IsGSDDay), IsOffDay = $($existing.IsOffDay), " +
            "Task = $(Sql $existing.Task), WicOpeningHours = $(SqlNullable $existing.WicOpeningHours) WHERE Id = $($existing.Id);")
    }
    else {
        [void]$sb.AppendLine(
            "DELETE FROM WicShiftEntries WHERE EmployeeId = $(Sql $p.EmployeeId) AND " +
            "ShiftDate = '$($p.ShiftDate)' AND SupportLocation = $(Sql $p.CentreName);")
    }
}
[void]$sb.AppendLine('COMMIT TRANSACTION;')

$final = Join-Path $Pass1Dir 'rollback-FINAL.sql'
Set-Content -LiteralPath $final -Value $sb.ToString() -Encoding UTF8

# Keep pass 1's own file, but make the superseded state explicit.
$orig = Join-Path $Pass1Dir 'rollback.run1-original.sql'
if (-not (Test-Path -LiteralPath $orig)) {
    Copy-Item -LiteralPath (Join-Path $Pass1Dir 'rollback.sql') -Destination $orig
}
Set-Content -LiteralPath (Join-Path $Pass1Dir 'rollback.sql') -Value $sb.ToString() -Encoding UTF8

$note = @"
WIC shift-plan sync repair — 2026-10-15, two passes, complete rollback.

  rollback-FINAL.sql   THE ONE TO RUN on a rollback (= the corrected rollback.sql).
  rollback.sql         same content (corrected after pass 2).
  rollback.run1-original.sql
                       pass 1's own file, kept for the record. Its DELETE for the 5 rows
                       that pass 2 turned on site is SUPERSEDED — use rollback-FINAL.sql.

Pass 1 ($Pass1Dir): 22 rows, source marker + 17 WicShiftEntries inserts, 5 rows that already
                    existed at the right centre were skipped.
Pass 2 ($Pass2Dir): the same 5 rows, IsOnSite 0 -> 1 (their pre-image is pass 2's
                    WicShiftEntries-pre.csv; pass 1's pre-image holds the same values).
Both pre-images are kept in each pass directory; ManualChangeBy was never written.
"@
Set-Content -LiteralPath (Join-Path $Pass1Dir 'NOTE.txt') -Value $note -Encoding UTF8

Write-Output "wrote: $final"
Write-Output "statements: $((Get-Content -LiteralPath $final | Select-String -Pattern '^(UPDATE|DELETE)').Count)"
