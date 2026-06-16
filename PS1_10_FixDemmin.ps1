# PS1_10_FixDemmin.ps1
# One-shot fix: stamps DE_Demmin_Hanse on the row whose LocationCode
# has a space after the tilde ("DE~17109~ Demmin~Am Hanseufer 2").
# Then verifies both expected counts (42 and 0).

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function Query($sql, $label) {
    Write-Host ""
    Write-Host ">>> $label" -ForegroundColor Cyan
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        $cmd.CommandTimeout = 30
        $r = $cmd.ExecuteReader()
        $rows = @()
        while ($r.Read()) {
            $row = [ordered]@{}
            for ($i = 0; $i -lt $r.FieldCount; $i++) {
                $v = $r.GetValue($i)
                $row[$r.GetName($i)] = if ($v -is [DBNull]) { "NULL" } else { $v }
            }
            $rows += [pscustomobject]$row
        }
        $r.Close()
        $conn.Close()
        if ($rows.Count -eq 0) {
            Write-Host "(no rows)" -ForegroundColor Yellow
        } else {
            $rows | Format-Table -AutoSize -Wrap
        }
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
    }
}

# --- Fix ---------------------------------------------------------------------

Write-Host ""
Write-Host ">>> UPDATE DE_Demmin_Hanse (space after tilde)" -ForegroundColor Cyan
try {
    $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "UPDATE WicLocations SET LocationCodeLegacy = @legacy WHERE LocationCode LIKE @pattern AND (LocationCodeLegacy IS NULL OR LocationCodeLegacy <> @legacy)"
    $cmd.Parameters.AddWithValue("@legacy",  "DE_Demmin_Hanse") | Out-Null
    $cmd.Parameters.AddWithValue("@pattern", "DE~17109~ Demmin~%") | Out-Null
    $n = $cmd.ExecuteNonQuery()
    $conn.Close()
    if ($n -gt 0) {
        Write-Host "OK ($n row updated)" -ForegroundColor Green
    } else {
        Write-Host "WARNING: 0 rows matched - check LocationCode in WicLocations" -ForegroundColor Yellow
    }
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
}

# --- Verify ------------------------------------------------------------------

$sqlV1 = "SELECT COUNT(*) AS LocationsWithLegacyCode FROM WicLocations WHERE LocationCodeLegacy IS NOT NULL"

$sqlV2 = "SELECT COUNT(*) AS UnresolvedAssignmentRows FROM WicAgentAssignments waa WHERE NOT EXISTS (SELECT 1 FROM WicLocations wl WHERE wl.LocationCode = waa.LocationCode OR wl.LocationCodeLegacy = waa.LocationCode)"

Query $sqlV1 "WicLocations rows with LocationCodeLegacy set (expected: 42)"
Query $sqlV2 "Unresolved WicAgentAssignments rows (expected: 0)"

Write-Host ""
Write-Host "=== PS1_10 complete ===" -ForegroundColor Green
