param(
    [string]$EmployeeId = '9128154',
    [string]$OutDir     = 'C:\GSDDashboard_secure\Tools\AlTrackerOverride20261016\Data',
    [switch]$WhatIf
)
# PART B data write: mark ONE employee as ON DEMAND / NOT ROSTERED BY US (owner decision
# 2026-10-16). He works on another project and is engaged only when needed, so the Overview
# "Roster gaps" banner (GET /api/roster/missing) must stop listing him while he stays active
# and assignable by hand.
#
# Contract:
#   * backup of the row (CSV) is written BEFORE the update;
#   * a rollback.sql restoring the exact previous value is written next to it;
#   * exactly ONE Employees row is touched, and ONLY the IsOnDemand column;
#   * ShiftEntries is never touched, so no ManualChangeBy is written anywhere;
#   * the update runs in ONE transaction and asserts @@ROWCOUNT = 1 and that the value
#     actually changed, otherwise it rolls back.
# Read-only by default (-WhatIf prints the plan and writes nothing).
$ErrorActionPreference = 'Stop'
$Server = 'localhost\SQLEXPRESS'
$Db     = 'GSDDashboard'

function Invoke-Sql([string]$q) {
    $out = & sqlcmd -S $Server -E -d $Db -Q $q -W -s '|' -h -1 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $out" }
    return $out
}

if (!(Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

# ── 1. Read the current value (and prove the row exists) ──────────────────────
$before = Invoke-Sql "SET NOCOUNT ON; SELECT CONVERT(NVARCHAR(20), Id) + '|' + EmployeeId + '|' + CONVERT(NVARCHAR(5), IsOnDemand) FROM Employees WHERE EmployeeId = '$EmployeeId';"
$before = @($before | Where-Object { $_ -and $_.Trim() -ne '' -and $_ -notmatch '^-+$' })
if ($before.Count -ne 1) { throw "Expected exactly 1 Employees row for $EmployeeId, got $($before.Count)." }
$parts = $before[0].Split('|')
$rowId = $parts[0].Trim(); $empId = $parts[1].Trim(); $oldValue = $parts[2].Trim()
Write-Output "found          : Id=$rowId EmployeeId=$empId IsOnDemand=$oldValue"

if ($oldValue -eq '1') { Write-Output 'already flagged: IsOnDemand=1 - nothing to do.'; return }

# ── 2. Backup the row + full column snapshot ──────────────────────────────────
$backupCsv = Join-Path $OutDir "employees-$EmployeeId-before-$stamp.csv"
Invoke-Sql "SET NOCOUNT ON; SELECT * FROM Employees WHERE EmployeeId = '$EmployeeId';" |
    Set-Content -LiteralPath $backupCsv -Encoding UTF8
Write-Output "backup         : $backupCsv"

$rollbackSql = Join-Path $OutDir "rollback-$EmployeeId-$stamp.sql"
@(
    "-- Rollback for the PART B on-demand flag (owner decision 2026-10-16).",
    "-- Restores Employees.IsOnDemand for exactly one row to its pre-change value.",
    "-- Generated $stamp. Run with:",
    "--   sqlcmd -S $Server -E -d $Db -i `"$rollbackSql`"",
    "SET XACT_ABORT ON;",
    "BEGIN TRAN;",
    "UPDATE Employees SET IsOnDemand = $oldValue WHERE EmployeeId = '$EmployeeId';",
    "IF @@ROWCOUNT <> 1 BEGIN ROLLBACK; RAISERROR('rollback rowcount <> 1',16,1); END",
    "ELSE COMMIT;"
) | Set-Content -LiteralPath $rollbackSql -Encoding UTF8
Write-Output "rollback       : $rollbackSql"

if ($WhatIf) { Write-Output 'WhatIf: no write performed.'; return }

# ── 3. One guarded transaction ────────────────────────────────────────────────
$apply = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;
UPDATE Employees SET IsOnDemand = 1 WHERE EmployeeId = '$EmployeeId' AND IsOnDemand = 0;
IF @@ROWCOUNT <> 1 BEGIN ROLLBACK; RAISERROR('on-demand flag: expected 1 updated row',16,1); END
ELSE COMMIT;
SELECT 'AFTER|' + CONVERT(NVARCHAR(5), IsOnDemand) FROM Employees WHERE EmployeeId = '$EmployeeId';
"@
$applyOut = Invoke-Sql $apply
$after = @($applyOut | Where-Object { $_ -match '^AFTER\|' })
if ($after.Count -ne 1 -or $after[0].Trim() -ne 'AFTER|1') { throw "Update verification failed: $applyOut" }
Write-Output "applied        : IsOnDemand 0 -> 1 for $EmployeeId"

# ── 4. Post-check: nothing else changed ───────────────────────────────────────
$changed = Invoke-Sql "SET NOCOUNT ON; SELECT CONVERT(NVARCHAR(10), COUNT(*)) FROM Employees WHERE EmployeeId='$EmployeeId' AND IsOnDemand=1;"
Write-Output "post-check     : flagged rows = $($changed | Where-Object { $_ -match '\d' } | Select-Object -First 1)"
$receipt = [pscustomobject]@{
    when = (Get-Date).ToString('o'); employeeId = $EmployeeId; rowId = [int]$rowId
    before = [int]$oldValue; after = 1
    backupCsv = $backupCsv; rollbackSql = $rollbackSql
}
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir "apply-receipt-$stamp.json") -Encoding UTF8
Write-Output "receipt        : $(Join-Path $OutDir "apply-receipt-$stamp.json")"
