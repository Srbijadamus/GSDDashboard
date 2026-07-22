# PS1_69_UpdateShiftTypes20260714.ps1
# Updates existing ShiftEntries from WORKING/WIC_DUTY to correct absence type.
#
# Found in PS1_68 run -- these people have wrong ShiftType for 2026-07-14:
#   Sharon Huber          WORKING  -> SL
#   Dominik Bajic         WORKING  -> SL
#   Pascal Dutz           WORKING  -> SL
#   Anisha Nellikka Panikkan WORKING -> SL
#   Mark Bachmann         WIC_DUTY -> SL
#   Abdulrahman Aldera    WORKING  -> AL
#   Francois Sicot        WIC_DUTY -> AL
#   Kamil Filipowicz      WORKING  -> AL
#   Kavinraj Pathmanathan WORKING  -> AL
#   Boris Kostov          WORKING  -> OFF
#   Krishnendu Das        WORKING  -> OFF

$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
$TODAY   = "2026-07-14"

$updates = @(
    [PSCustomObject]@{ EmployeeId="9074576"; Name="Sharon Huber";              NewType="SL"  },
    [PSCustomObject]@{ EmployeeId="9126887"; Name="Dominik Bajic";             NewType="SL"  },
    [PSCustomObject]@{ EmployeeId="9074557"; Name="Pascal Dutz";               NewType="SL"  },
    [PSCustomObject]@{ EmployeeId="9124145"; Name="Anisha Nellikka Panikkan";  NewType="SL"  },
    [PSCustomObject]@{ EmployeeId="9122675"; Name="Mark Bachmann";             NewType="SL"  },
    [PSCustomObject]@{ EmployeeId="9125519"; Name="Abdulrahman Aldera";        NewType="AL"  },
    [PSCustomObject]@{ EmployeeId="9128153"; Name="Francois Sicot";            NewType="AL"  },
    [PSCustomObject]@{ EmployeeId="9112561"; Name="Kamil Filipowicz";          NewType="AL"  },
    [PSCustomObject]@{ EmployeeId="9128149"; Name="Kavinraj Pathmanathan";     NewType="AL"  },
    [PSCustomObject]@{ EmployeeId="9124690"; Name="Boris Kostov";              NewType="OFF" },
    [PSCustomObject]@{ EmployeeId="9084156"; Name="Krishnendu Das";            NewType="OFF" }
)

Write-Host ""
Write-Host "=== PS1_69: Update ShiftTypes $TODAY ===" -ForegroundColor Yellow
Write-Host "    5 SL + 4 AL + 2 OFF = 11 updates" -ForegroundColor Yellow
Write-Host ""

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
try { $conn.Open(); Write-Host "DB: OK" -ForegroundColor Green }
catch { Write-Host "ERROR: $_" -ForegroundColor Red; exit 1 }

# ── Preview current state ─────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Current state before update ---" -ForegroundColor Cyan

$idList     = ($updates | ForEach-Object { "'" + $_.EmployeeId + "'" }) -join ","
$previewSql = "SELECT EmployeeId, ShiftType FROM ShiftEntries WHERE ShiftDate = '$TODAY' AND EmployeeId IN ($idList)"
$previewCmd = New-Object System.Data.SqlClient.SqlCommand($previewSql, $conn)
$pr         = $previewCmd.ExecuteReader()
$currentMap = @{}
while ($pr.Read()) { $currentMap[[string]$pr["EmployeeId"]] = [string]$pr["ShiftType"] }
$pr.Close()

foreach ($u in $updates) {
    $cur = if ($currentMap.ContainsKey($u.EmployeeId)) { $currentMap[$u.EmployeeId] } else { "NOT FOUND" }
    Write-Host ("  {0,-35}  {1,-10} -> {2}" -f $u.Name, $cur, $u.NewType) -ForegroundColor Cyan
}

# ── UPDATE ────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- UPDATE ShiftEntries ---" -ForegroundColor Cyan

$updateSql = @"
UPDATE ShiftEntries
SET    ShiftType      = @NewType,
       RawValue       = @NewType,
       PreviousStatus = ShiftType
WHERE  EmployeeId = @EmployeeId
  AND  ShiftDate  = @ShiftDate
"@

$updated = 0
$failed  = 0

foreach ($u in $updates) {
    try {
        $cmd = New-Object System.Data.SqlClient.SqlCommand($updateSql, $conn)
        $cmd.Parameters.AddWithValue("@NewType",    $u.NewType)                  | Out-Null
        $cmd.Parameters.AddWithValue("@EmployeeId", $u.EmployeeId)               | Out-Null
        $cmd.Parameters.AddWithValue("@ShiftDate",  [datetime]::Parse($TODAY))   | Out-Null
        $rows = $cmd.ExecuteNonQuery()

        if ($rows -gt 0) {
            Write-Host ("  UPDATE  {0,-35}  {1,-10} -> {2}  ({3} row)" -f $u.Name, $currentMap[$u.EmployeeId], $u.NewType, $rows) -ForegroundColor Green
            $updated++
        } else {
            Write-Host ("  WARN    {0,-35}  0 rows affected (no ShiftEntry found?)" -f $u.Name) -ForegroundColor Yellow
        }
    } catch {
        Write-Host ("  ERROR   {0,-35}  {1}" -f $u.Name, $_) -ForegroundColor Red
        $failed++
    }
}

# ── Verify ────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Verify ---" -ForegroundColor Cyan

$vSql = @"
SELECT ShiftType, COUNT(*) AS Cnt
FROM ShiftEntries
WHERE ShiftDate = '$TODAY'
  AND ShiftType IN ('SL','AL','OFF','OL','CD','UL')
GROUP BY ShiftType
ORDER BY ShiftType
"@
$vCmd = New-Object System.Data.SqlClient.SqlCommand($vSql, $conn)
$vr   = $vCmd.ExecuteReader()
Write-Host "  ShiftEntries absence counts for ${TODAY}:" -ForegroundColor Cyan
$totSL=0; $totAL=0; $totOFF=0
while ($vr.Read()) {
    $st  = [string]$vr["ShiftType"]
    $cnt = [int]$vr["Cnt"]
    $ok  = ""
    if ($st -eq "SL"  -and $cnt -eq 6)  { $ok = " OK"; $totSL=$cnt  }
    if ($st -eq "AL"  -and $cnt -eq 12) { $ok = " OK"; $totAL=$cnt  }
    if ($st -eq "OFF" -and $cnt -eq 10) { $ok = " OK"; $totOFF=$cnt }
    if ($st -eq "SL"  -and $cnt -ne 6)  { $ok = " (expected 6)";  $totSL=$cnt  }
    if ($st -eq "AL"  -and $cnt -ne 12) { $ok = " (expected 12)"; $totAL=$cnt  }
    if ($st -eq "OFF" -and $cnt -ne 10) { $ok = " (expected 10)"; $totOFF=$cnt }
    $color = if ($ok -eq " OK") { "Green" } else { "Yellow" }
    Write-Host ("    {0,-6}  {1}{2}" -f $st, $cnt, $ok) -ForegroundColor $color
}
$vr.Close()

$conn.Close()

Write-Host ""
Write-Host ("=== SUMMARY: Updated={0}  Errors={1} ===" -f $updated, $failed) `
    -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host ""
Write-Host "Refresh browser -- Overview should now show SL=6  AL=12  OFF=10" -ForegroundColor Cyan
Write-Host ""
Write-Host "=== PS1_69 complete ===" -ForegroundColor Green
