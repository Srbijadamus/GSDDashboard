# PS1_AL_Import.ps1
# Import Annual Leave entries into ShiftEntries.
# ShiftType='AL' (full day) or 'HALF_AL' (0.5 day - Eva-Liane Schliwa only).
# Skips (EmployeeId, ShiftDate) that already have any AL/UL/HALF_AL row (any SourceSheet).
# One transaction, SET XACT_ABORT ON, count-guarded, ROLLBACK on any mismatch.

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"
Add-Type -AssemblyName "System.Data"
$cfg = Get-Content $appSettingsPath | ConvertFrom-Json
$cs  = $cfg.ConnectionStrings.DefaultConnection

function Open-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection($script:cs)
    $c.Open()
    return $c
}

function Invoke-Rows([string]$sql, [hashtable]$params = @{}) {
    $conn = Open-Conn
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($kv in $params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        $rows = @()
        $rdr  = $cmd.ExecuteReader()
        while ($rdr.Read()) {
            $row = @{}
            for ($i = 0; $i -lt $rdr.FieldCount; $i++) {
                $v = $rdr.GetValue($i)
                $rowVal = $v; if ($v -is [System.DBNull]) { $rowVal = $null }
                $row[$rdr.GetName($i)] = $rowVal
            }
            $rows += [PSCustomObject]$row
        }
        $rdr.Close()
        return $rows
    } finally { $conn.Close() }
}

# ── Entry data (EmployeeId, from DD.MM.YYYY, to DD.MM.YYYY, ShiftType) ────────
$alEntries = @(
    [PSCustomObject]@{ EmpId="9117836"; From="22.06.2026"; To="23.06.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9126880"; From="26.06.2026"; To="26.06.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9126880"; From="03.07.2026"; To="03.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9074381"; From="29.06.2026"; To="29.06.2026"; Type="HALF_AL" }
    [PSCustomObject]@{ EmpId="9120970"; From="02.07.2026"; To="03.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9125521"; From="03.07.2026"; To="03.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9125521"; From="10.07.2026"; To="10.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9133995"; From="06.07.2026"; To="17.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9092596"; From="08.07.2026"; To="17.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9130649"; From="13.07.2026"; To="17.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9114618"; From="08.07.2026"; To="10.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9114618"; From="15.07.2026"; To="17.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9114618"; From="22.07.2026"; To="24.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9114618"; From="29.07.2026"; To="31.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9125526"; From="17.07.2026"; To="17.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9125526"; From="23.07.2026"; To="24.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="4451020"; From="27.07.2026"; To="31.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9125517"; From="27.07.2026"; To="31.07.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9120980"; From="03.08.2026"; To="07.08.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9126887"; From="03.08.2026"; To="05.08.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9126887"; From="10.08.2026"; To="12.08.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9129429"; From="07.08.2026"; To="19.08.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9120970"; From="06.08.2026"; To="07.08.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9130649"; From="14.09.2026"; To="18.09.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9120970"; From="14.09.2026"; To="18.09.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9120970"; From="26.10.2026"; To="30.10.2026"; Type="AL"      }
    [PSCustomObject]@{ EmpId="9120970"; From="28.12.2026"; To="30.12.2026"; Type="AL"      }
)

# ── Expand entries to one row per weekday ──────────────────────────────────────
$allExpanded = @()
foreach ($e in $alEntries) {
    $fromDt = [DateTime]::ParseExact($e.From, "dd.MM.yyyy",
                  [System.Globalization.CultureInfo]::InvariantCulture)
    $toDt   = [DateTime]::ParseExact($e.To,   "dd.MM.yyyy",
                  [System.Globalization.CultureInfo]::InvariantCulture)
    for ($d = $fromDt; $d -le $toDt; $d = $d.AddDays(1)) {
        if ($d.DayOfWeek -ne [DayOfWeek]::Saturday -and
            $d.DayOfWeek -ne [DayOfWeek]::Sunday) {
            $allExpanded += [PSCustomObject]@{
                EmpId     = $e.EmpId
                ShiftDate = $d
                ShiftType = $e.Type
            }
        }
    }
}

# ── Build query parameters: distinct employee IDs + overall date span ──────────
$empIdList = @($alEntries | Select-Object -ExpandProperty EmpId -Unique)
$existParams = @{}
$empParamNames = @()
$pi = 0
foreach ($id in $empIdList) {
    $pn = "em$pi"
    $empParamNames += "@$pn"
    $existParams[$pn] = $id
    $pi++
}
$inClause = $empParamNames -join ","

$sortedDates  = @($allExpanded | Select-Object -ExpandProperty ShiftDate | Sort-Object)
$existParams["minDate"] = $sortedDates[0].ToString("yyyy-MM-dd")
$existParams["maxDate"] = $sortedDates[-1].ToString("yyyy-MM-dd")

$existDetailSql = @"
SELECT EmployeeId, ShiftDate
FROM ShiftEntries
WHERE EmployeeId IN ($inClause)
  AND ShiftDate BETWEEN @minDate AND @maxDate
  AND ShiftType IN ('AL', 'UL', 'HALF_AL')
"@

$existCountSql = @"
SELECT COUNT(*) AS Cnt
FROM ShiftEntries
WHERE EmployeeId IN ($inClause)
  AND ShiftDate BETWEEN @minDate AND @maxDate
  AND ShiftType IN ('AL', 'UL', 'HALF_AL')
"@

# ══════════════════════════════════════════════════════════════════════════════
# PHASE 1 -- EXPANSION PLAN
# ══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host " PHASE 1 - EXPANSION PLAN" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host "  Expanded $($alEntries.Count) entries to $($allExpanded.Count) weekday rows."
Write-Host "  Querying existing AL/UL/HALF_AL rows for $($empIdList.Count) employees..." -NoNewline

$existingRows  = Invoke-Rows $existDetailSql $existParams
$preExistCount = $existingRows.Count
Write-Host " $preExistCount found (will be skipped)." -ForegroundColor DarkGray

# Build O(1) lookup set: "EmployeeId|yyyy-MM-dd"
$existingSet = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($row in $existingRows) {
    [void]$existingSet.Add("$($row.EmployeeId)|$($row.ShiftDate.ToString('yyyy-MM-dd'))")
}

# Split expanded rows into insert vs skip
$toInsert = @()
$toSkip   = @()
foreach ($row in $allExpanded) {
    $key = "$($row.EmpId)|$($row.ShiftDate.ToString('yyyy-MM-dd'))"
    if ($existingSet.Contains($key)) {
        $toSkip += $row
    } else {
        $toInsert += $row
    }
}
$expectedInserts = $toInsert.Count

# Per-employee summary
Write-Host ""
$h1Fmt = "{0,-12} {1,-8} {2,-6} {3,-8} {4}"
Write-Host ($h1Fmt -f "EmployeeId", "Weekdays", "Skip", "Insert", "Skipped dates") -ForegroundColor Cyan
Write-Host ($h1Fmt -f ("-"*12), ("-"*8), ("-"*6), ("-"*8), ("-"*40)) -ForegroundColor DarkGray

foreach ($empId in ($empIdList | Sort-Object)) {
    $empExpanded  = @($allExpanded | Where-Object { $_.EmpId -eq $empId })
    $empSkip      = @($toSkip     | Where-Object { $_.EmpId -eq $empId })
    $empInsert    = @($toInsert   | Where-Object { $_.EmpId -eq $empId })
    $skipDates    = ($empSkip | ForEach-Object { $_.ShiftDate.ToString("dd.MM") }) -join ", "
    $skipStr      = ""
    if ($skipDates -ne "") { $skipStr = "skip: $skipDates" }
    $color = "Green"
    if ($empSkip.Count -gt 0) { $color = "Yellow" }
    Write-Host ($h1Fmt -f $empId, $empExpanded.Count, $empSkip.Count, $empInsert.Count, $skipStr) -ForegroundColor $color
}
Write-Host ""
Write-Host ("  Total weekday rows: {0}  |  Skip (already in DB): {1}  |  Insert: {2}" -f `
    $allExpanded.Count, $toSkip.Count, $expectedInserts) -ForegroundColor Cyan

if ($expectedInserts -eq 0) {
    Write-Host ""
    Write-Host "Nothing to insert -- all rows already present. DONE." -ForegroundColor Green
    exit 0
}

# ══════════════════════════════════════════════════════════════════════════════
# PHASE 2 -- INSERT (one count-guarded transaction)
# ══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host " PHASE 2 - INSERT ($expectedInserts rows, one transaction)" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta

$conn2 = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn2.Open()
$tx = $null

try {
    $xaCmd = $conn2.CreateCommand()
    $xaCmd.CommandText = "SET XACT_ABORT ON"
    [void]$xaCmd.ExecuteNonQuery()
    $tx = $conn2.BeginTransaction()

    # In-tx guard: existing AL count must not have changed since Phase 1
    $chkCmd = $conn2.CreateCommand(); $chkCmd.Transaction = $tx
    $chkCmd.CommandText = $existCountSql
    foreach ($kv in $existParams.GetEnumerator()) {
        [void]$chkCmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    $chkExistCount = [int]$chkCmd.ExecuteScalar()

    if ($chkExistCount -ne $preExistCount) {
        throw "Guard: pre-tx existing count=$preExistCount, in-tx=$chkExistCount. Concurrent change detected -- aborting."
    }
    Write-Host "  In-tx guard passed (existing=$preExistCount). Inserting..." -ForegroundColor DarkGray

    # Insert rows
    $totalInserted = 0
    $insertSql = @"
INSERT INTO ShiftEntries
    (EmployeeId, ShiftDate, ShiftType, SourceSheet, AutoGenerated, IsWicDuty)
VALUES
    (@empId, @shiftDate, @shiftType, 'AL_IMPORT', 1, 0)
"@
    foreach ($row in $toInsert) {
        $insCmd = $conn2.CreateCommand(); $insCmd.Transaction = $tx
        $insCmd.CommandText = $insertSql
        [void]$insCmd.Parameters.AddWithValue("@empId",     $row.EmpId)
        [void]$insCmd.Parameters.AddWithValue("@shiftDate", $row.ShiftDate)
        [void]$insCmd.Parameters.AddWithValue("@shiftType", $row.ShiftType)
        $n = $insCmd.ExecuteNonQuery()
        if ($n -ne 1) {
            throw "INSERT returned $n rows affected (expected 1) for EmpId=$($row.EmpId) Date=$($row.ShiftDate.ToString('yyyy-MM-dd'))."
        }
        $totalInserted++
    }

    if ($totalInserted -ne $expectedInserts) {
        throw "Guard: expected=$expectedInserts inserted=$totalInserted mismatch. ROLLBACK."
    }

    $tx.Commit()
    Write-Host ("  Inserted: {0} rows. Transaction committed." -f $totalInserted) -ForegroundColor Green

} catch {
    if ($null -ne $tx) { try { $tx.Rollback() } catch {} }
    Write-Host ""
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host "Transaction rolled back. No rows written." -ForegroundColor Red
    throw
} finally {
    $conn2.Close()
}

# ══════════════════════════════════════════════════════════════════════════════
# PHASE 3 -- VERIFICATION
# ══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host " PHASE 3 - VERIFICATION" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta

# Re-query all AL rows for our employees (any SourceSheet) to verify
$verRows = Invoke-Rows $existDetailSql $existParams
$verSet  = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($row in $verRows) {
    [void]$verSet.Add("$($row.EmployeeId)|$($row.ShiftDate.ToString('yyyy-MM-dd'))")
}

# Per-employee: verify each inserted row is now present
$errCount = 0
$h3Fmt = "{0,-12} {1,-10} {2,-10} {3}"
Write-Host ""
Write-Host ($h3Fmt -f "EmployeeId", "Expected", "Found", "Status") -ForegroundColor Cyan
Write-Host ($h3Fmt -f ("-"*12), ("-"*10), ("-"*10), ("-"*20)) -ForegroundColor DarkGray

foreach ($empId in ($empIdList | Sort-Object)) {
    $empInserted = @($toInsert | Where-Object { $_.EmpId -eq $empId })
    if ($empInserted.Count -eq 0) { continue }   # no inserts for this emp, skip

    $empFound    = 0
    $empMissing  = @()
    foreach ($row in $empInserted) {
        $key = "$($row.EmpId)|$($row.ShiftDate.ToString('yyyy-MM-dd'))"
        if ($verSet.Contains($key)) {
            $empFound++
        } else {
            $empMissing += $row.ShiftDate.ToString("dd.MM.yyyy")
        }
    }

    $status = "OK"
    $color  = "Green"
    if ($empMissing.Count -gt 0) {
        $status = "FAIL: missing $($empMissing -join ',')"
        $color  = "Red"
        $errCount++
    }
    Write-Host ($h3Fmt -f $empId, $empInserted.Count, $empFound, $status) -ForegroundColor $color
}

# Skipped rows: confirm they are still there (not accidentally deleted)
if ($toSkip.Count -gt 0) {
    Write-Host ""
    Write-Host "  Skipped rows (pre-existing) -- confirming still present:" -ForegroundColor DarkGray
    foreach ($row in $toSkip) {
        $key = "$($row.EmpId)|$($row.ShiftDate.ToString('yyyy-MM-dd'))"
        $present = $verSet.Contains($key)
        $skipStatus = "still present"
        $skipColor  = "DarkGray"
        if (-not $present) {
            $skipStatus = "MISSING - unexpected!"
            $skipColor  = "Red"
            $errCount++
        }
        Write-Host ("    EmpId={0}  Date={1}  {2}" -f $row.EmpId, $row.ShiftDate.ToString("dd.MM.yyyy"), $skipStatus) -ForegroundColor $skipColor
    }
}

# Totals
Write-Host ""
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host " TOTALS" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host ("  Expanded weekday rows : {0}" -f $allExpanded.Count)
Write-Host ("  Skipped (pre-existing): {0}" -f $toSkip.Count)    -ForegroundColor DarkGray
Write-Host ("  Inserted              : {0}" -f $totalInserted)    -ForegroundColor Green
Write-Host ("  Verify errors         : {0}" -f $errCount)         -ForegroundColor $(if ($errCount -eq 0) { "Green" } else { "Red" })

if ($errCount -eq 0) {
    Write-Host ""
    Write-Host "ALL DONE. $totalInserted AL rows inserted and verified." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "VERIFY FAILED: $errCount error(s). Investigate above." -ForegroundColor Red
}
