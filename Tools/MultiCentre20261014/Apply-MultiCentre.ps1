# Apply-MultiCentre.ps1
# The EXECUTIVE write of the multi-centre fix (items 1 and 2 of the task):
#
#   * every IsOnSite=1 WicShiftEntries row of a 2+ centre day gets ITS OWN centre's
#     working hours for that date (item 1: Am Hanseufer 10:00-12:00, Woldeforster 13:00-15:00);
#   * the single daily ShiftEntries row gets the span (earliest WIC start .. latest WIC end,
#     i.e. 10:00-15:00) and the marker "span 2 centra (DEV 14.10)" is APPENDED to
#     ManualChangeNote. ManualChangeBy / ManualChangeAt are never written.
#
# ONE transaction. Backup + rollback.sql under
#   C:\ProgramData\PulseRollback\multi-centre-<timestamp>\
# The plan is recomputed from the live database; the whole run is a no-op when the
# analysis finds no day whose ShiftEntries does not cover all of its WIC parts.
#
# RTM KID S60028 is never used as a session id: this script does not log in anywhere.
param(
    [string]$From       = '2026-10-14',
    [string]$BackupRoot = 'C:\ProgramData\PulseRollback',
    [switch]$WhatIf
)
$ErrorActionPreference = 'Stop'
. 'C:\GSDDashboard_secure\Tools\MultiCentre20261014\MultiCentre.Common.ps1'

function Invoke-TxQuery($conn, $tx, [string]$sql) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.CommandTimeout = 300
    if ($tx) { $cmd.Transaction = $tx }
    $r = $cmd.ExecuteReader()
    $dt = New-Object System.Data.DataTable
    $dt.Load($r)
    return ,$dt
}
function Invoke-TxNonQuery($conn, $tx, [string]$sql) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.CommandTimeout = 300
    if ($tx) { $cmd.Transaction = $tx }
    return $cmd.ExecuteNonQuery()
}

$conn = Get-RtmConnection
$log  = New-Object System.Collections.ArrayList
function Say([string]$m) { Write-Output $m; [void]$log.Add($m) }

try {
    # ------------------------------------------------------------ 1. plan (read only)
    $analysis = Get-McAnalysis $conn ([datetime]$From)
    $groups   = @($analysis.Results)
    $plan     = @($groups | Where-Object { $_.SpanMissing })
    Say ("PLAN from >= {0}: multi-centre employee/days {1}; days whose ShiftEntries does not span all WIC parts {2}" -f $From, $groups.Count, $plan.Count)

    if ($plan.Count -eq 0) {
        Say 'Nothing to do - no violating day found. No write performed.'
        return
    }

    foreach ($p in $plan) {
        Say ("  {0}  {1}  centres={2}  [{3}] -> [{4}]" -f $p.EmployeeId, $p.ShiftDate, $p.Centres, $p.OldShift, $p.NewShift)
        foreach ($u in $p.WicUpdates) {
            $mark = '  (already correct)'
            if ($u.Changed) { $mark = '  *UPDATE*' }
            Say ("      WicShiftEntries Id={0} {1}: '{2}' -> '{3}'{4}" -f $u.WicId, $u.SupportLocation, $u.Old, $u.New, $mark)
        }
        Say ("      ShiftEntries Id={0} note += '{1}'" -f $p.ShiftEntryId, $script:McMarker)
    }

    # ------------------------------------------------------------ 2. backup dir
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $root  = Join-Path $BackupRoot ("multi-centre-$stamp")
    if (Test-Path -LiteralPath $root) { throw "Backup directory already exists: $root" }
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Say ("BACKUP {0}" -f $root)
    $log | Set-Content -LiteralPath (Join-Path $root 'plan.txt') -Encoding UTF8

    if ($WhatIf) { Say 'WHATIF - no transaction opened, no write performed.'; return }

    # ------------------------------------------------------------ 3. one transaction
    $tx = $conn.BeginTransaction([System.Data.IsolationLevel]::Serializable)

    $rollback = New-Object System.Collections.ArrayList
    [void]$rollback.Add("/* rollback.sql - multi-centre fix $stamp")
    [void]$rollback.Add("   Restores the pre-image of every row touched by Apply-MultiCentre.ps1.")
    [void]$rollback.Add("   Guarded on Id AND the value this run wrote, so a later human edit is never undone. */")
    [void]$rollback.Add('BEGIN TRANSACTION;')

    $wicPre = New-Object System.Collections.ArrayList
    $sePre  = New-Object System.Collections.ArrayList
    $updWic = 0
    $updSe  = 0

    foreach ($p in $plan) {
        $emp  = Get-RtmSql $p.EmployeeId
        $date = $p.ShiftDate

        foreach ($u in $p.WicUpdates) {
            if (-not $u.New) { continue }

            $sqlCur = "SELECT Id, EmployeeId, ShiftDate, SupportLocation, WorkingShift, IsOnSite, LocationCode, Task FROM WicShiftEntries WHERE Id = " + $u.WicId
            $cur = Invoke-TxQuery $conn $tx $sqlCur
            if ($cur.Rows.Count -ne 1) { throw "WicShiftEntries Id $($u.WicId) disappeared - aborting." }
            $row = $cur.Rows[0]
            [void]$wicPre.Add([pscustomobject]@{
                Id = $row.Id; EmployeeId = $row.EmployeeId; ShiftDate = ([datetime]$row.ShiftDate).ToString('yyyy-MM-dd')
                SupportLocation = $row.SupportLocation; WorkingShiftOld = $row.WorkingShift; WorkingShiftNew = $u.New
                IsOnSite = $row.IsOnSite; LocationCode = $row.LocationCode; Task = $row.Task
            })
            if ([string]$row.WorkingShift -ne [string]$u.Old) {
                throw "WicShiftEntries Id $($u.WicId) changed since the plan (expected '$($u.Old)', found '$($row.WorkingShift)') - aborting."
            }
            if ([string]$u.Old -eq [string]$u.New) { continue }

            [void]$rollback.Add("UPDATE WicShiftEntries SET WorkingShift = N'" + (Get-RtmSql $u.Old) + "' WHERE Id = " + $u.WicId + " AND WorkingShift = N'" + (Get-RtmSql $u.New) + "';")
            $sqlUpd = "UPDATE WicShiftEntries SET WorkingShift = N'" + (Get-RtmSql $u.New) + "' WHERE Id = " + $u.WicId + " AND WorkingShift = N'" + (Get-RtmSql $u.Old) + "';"
            $updWic += Invoke-TxNonQuery $conn $tx $sqlUpd
        }

        if (-not $p.ShiftEntryId) { throw "No ShiftEntries row for $emp on $date - aborting." }
        $sqlSe = "SELECT Id, EmployeeId, ShiftStart, ShiftEnd, ManualChangeNote, ManualChangeBy, ManualChangeAt, ShiftType FROM ShiftEntries WHERE Id = " + $p.ShiftEntryId
        $seCur = Invoke-TxQuery $conn $tx $sqlSe
        if ($seCur.Rows.Count -ne 1) { throw "ShiftEntries Id $($p.ShiftEntryId) disappeared - aborting." }
        $se = $seCur.Rows[0]

        $seOld = "$($se.ShiftStart)-$($se.ShiftEnd)"
        if ($seOld -ne $p.OldShift) { throw "ShiftEntries Id $($p.ShiftEntryId) changed since the plan (expected '$($p.OldShift)', found '$seOld') - aborting." }
        if ([string]$se.ManualChangeNote -ne $p.OldNote) { throw "ShiftEntries Id $($p.ShiftEntryId) ManualChangeNote changed since the plan - aborting." }

        $newStart = $p.NewShift.Split('-')[0]
        $newEnd   = $p.NewShift.Split('-')[1]

        [void]$sePre.Add([pscustomobject]@{
            Id = $se.Id; EmployeeId = $se.EmployeeId; ShiftDate = $date
            ShiftStartOld = $se.ShiftStart; ShiftEndOld = $se.ShiftEnd
            ShiftStartNew = $newStart; ShiftEndNew = $newEnd
            ManualChangeBy = $se.ManualChangeBy; ManualChangeAt = $se.ManualChangeAt
            ManualChangeNoteOld = $se.ManualChangeNote; ManualChangeNoteNew = $p.NewNote
        })

        $rollbackSe = "UPDATE ShiftEntries SET ShiftStart = N'" + (Get-RtmSql $se.ShiftStart) + "', ShiftEnd = N'" + (Get-RtmSql $se.ShiftEnd) +
                      "', ManualChangeNote = N'" + (Get-RtmSql ([string]$se.ManualChangeNote)) + "' WHERE Id = " + $se.Id +
                      " AND ShiftStart = N'" + (Get-RtmSql $newStart) + "' AND ShiftEnd = N'" + (Get-RtmSql $newEnd) + "';"
        [void]$rollback.Add($rollbackSe)

        $sqlSeUpd = "UPDATE ShiftEntries SET ShiftStart = N'" + (Get-RtmSql $newStart) + "', ShiftEnd = N'" + (Get-RtmSql $newEnd) +
                    "', ManualChangeNote = N'" + (Get-RtmSql $p.NewNote) + "' WHERE Id = " + $se.Id +
                    " AND ShiftStart = N'" + (Get-RtmSql $se.ShiftStart) + "' AND ShiftEnd = N'" + (Get-RtmSql $se.ShiftEnd) + "';"
        $updSe += Invoke-TxNonQuery $conn $tx $sqlSeUpd
    }

    # ------------------------------------------------------------ 4. verify inside the tx
    $problems = New-Object System.Collections.ArrayList
    foreach ($p in $plan) {
        $emp  = Get-RtmSql $p.EmployeeId
        $sqlRows = "SELECT Id, SupportLocation, WorkingShift, IsOnSite FROM WicShiftEntries WHERE EmployeeId = N'" + $emp + "' AND ShiftDate = '" + $date + "' ORDER BY Id"
        $sqlRows = "SELECT Id, SupportLocation, WorkingShift, IsOnSite FROM WicShiftEntries WHERE EmployeeId = N'" + $emp + "' AND ShiftDate = '" + $p.ShiftDate + "' ORDER BY Id"
        $rows = Invoke-TxQuery $conn $tx $sqlRows

        foreach ($u in $p.WicUpdates) {
            $r = @($rows.Rows | Where-Object { $_.Id -eq $u.WicId })
            if ($r.Count -ne 1) { [void]$problems.Add("WIC $($u.WicId) missing"); continue }
            if ([string]$r[0].WorkingShift -ne [string]$u.New) { [void]$problems.Add("WIC $($u.WicId) = '$($r[0].WorkingShift)' != '$($u.New)'") }
            if (-not [bool]$r[0].IsOnSite) { [void]$problems.Add("WIC $($u.WicId) IsOnSite=0") }
        }

        $sqlSe2 = "SELECT ShiftStart, ShiftEnd, ManualChangeNote, ManualChangeBy FROM ShiftEntries WHERE Id = " + $p.ShiftEntryId
        $s = (Invoke-TxQuery $conn $tx $sqlSe2).Rows[0]
        if ("$($s.ShiftStart)-$($s.ShiftEnd)" -ne $p.NewShift) { [void]$problems.Add("ShiftEntries $($p.ShiftEntryId) = '$($s.ShiftStart)-$($s.ShiftEnd)' != '$($p.NewShift)'") }
        if ([string]$s.ManualChangeNote -notlike "*$($script:McMarker)*") { [void]$problems.Add("ShiftEntries $($p.ShiftEntryId) note missing marker") }
        if ($p.ManualChangeBy -and ([string]$s.ManualChangeBy -ne $p.ManualChangeBy)) { [void]$problems.Add("ShiftEntries $($p.ShiftEntryId) ManualChangeBy changed") }
    }

    if ($problems.Count -gt 0) {
        $tx.Rollback()
        Say 'ROLLBACK - in-transaction verification failed:'
        $problems | ForEach-Object { Say "   $_" }
        throw 'Verification failed; transaction rolled back.'
    }

    # ------------------------------------------------------------ 5. commit
    [void]$rollback.Add('COMMIT;')
    $tx.Commit()
    Say ("COMMITTED  WicShiftEntries updated {0}, ShiftEntries updated {1}" -f $updWic, $updSe)

    # ------------------------------------------------------------ 6. evidence files
    if ($wicPre.Count -gt 0) { $wicPre | Export-Csv -LiteralPath (Join-Path $root 'WicShiftEntries-pre.csv') -NoTypeInformation -Encoding UTF8 }
    if ($sePre.Count  -gt 0) { $sePre  | Export-Csv -LiteralPath (Join-Path $root 'ShiftEntries-pre.csv')        -NoTypeInformation -Encoding UTF8 }
    $groups | Select-Object EmployeeId, IsActive, ShiftDate, DOW, CentreCount, Centres, WicDetail, ShiftEntryId, ShiftType, ManualChangeBy, OldShift, NewShift, OldNote, NewNote, SpanMissing, WicFixCount, WicFixDetail |
        Export-Csv -LiteralPath (Join-Path $root 'analysis-all-multicentre.csv') -NoTypeInformation -Encoding UTF8
    $plan | Select-Object EmployeeId, ShiftDate, Centres, WicDetail, ShiftEntryId, OldShift, NewShift, OldNote, NewNote, ManualChangeBy |
        Export-Csv -LiteralPath (Join-Path $root 'plan.csv') -NoTypeInformation -Encoding UTF8
    ($rollback -join "`r`n") | Set-Content -LiteralPath (Join-Path $root 'rollback.sql') -Encoding UTF8
    $log | Set-Content -LiteralPath (Join-Path $root 'apply-log.txt') -Encoding UTF8

    $lines = New-Object System.Collections.ArrayList
    foreach ($f in (Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name)) {
        [void]$lines.Add("$((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash)  $($f.Name)")
    }
    ($lines -join "`r`n") | Set-Content -LiteralPath (Join-Path $root 'SHA256SUMS.txt') -Encoding UTF8
    Say ("EVIDENCE {0}" -f $root)
}
catch {
    Say ("FAILED: {0}" -f $_.Exception.Message)
    throw
}
finally { $conn.Close() }
