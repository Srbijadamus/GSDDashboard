# PS1_Step2_FixNames.ps1
# Reads unresolved names from WicAgentAssignments, AgentReachableCities, WicPipeline.
# Tries to match each to an active Employees.FullName via:
#   1. Normalized (accent-stripped) case-insensitive exact match
#   2. Normalized edit-distance <= 2 to exactly one active employee (fuzzy)
# Group (a) matches: auto-UPDATE in one transaction.
# Group (b) no-matches: listed, NOT deleted.

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
        $rdr = $cmd.ExecuteReader()
        while ($rdr.Read()) {
            $row = @{}
            for ($i = 0; $i -lt $rdr.FieldCount; $i++) {
                $v = $rdr.GetValue($i)
                $row[$rdr.GetName($i)] = if ($v -is [System.DBNull]) { $null } else { $v }
            }
            $rows += [PSCustomObject]$row
        }
        $rdr.Close()
        return $rows
    } finally { $conn.Close() }
}

function Exec-Tx([string]$sql, [hashtable]$params,
                 [System.Data.SqlClient.SqlConnection]$conn,
                 [System.Data.SqlClient.SqlTransaction]$tx) {
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx
    $cmd.CommandText = $sql
    foreach ($kv in $params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    return $cmd.ExecuteNonQuery()
}

function Strip-Accents([string]$s) {
    $n = $s.Normalize([System.Text.NormalizationForm]::FormD)
    $sb = [System.Text.StringBuilder]::new()
    foreach ($c in $n.ToCharArray()) {
        if ([System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($c) -ne
            [System.Globalization.UnicodeCategory]::NonSpacingMark) { [void]$sb.Append($c) }
    }
    return $sb.ToString()
}

function Normalize([string]$s) {
    return (Strip-Accents $s).ToLowerInvariant().Trim()
}

# Levenshtein distance
function Edit-Distance([string]$a, [string]$b) {
    $m = $a.Length; $n = $b.Length
    $d = New-Object 'int[,]' ($m+1), ($n+1)
    for ($i = 0; $i -le $m; $i++) { $d[$i,0] = $i }
    for ($j = 0; $j -le $n; $j++) { $d[0,$j] = $j }
    for ($i = 1; $i -le $m; $i++) {
        for ($j = 1; $j -le $n; $j++) {
            $cost = if ($a[$i-1] -eq $b[$j-1]) { 0 } else { 1 }
            $del  = $d[$i-1,$j]   + 1
            $ins  = $d[$i,$j-1]   + 1
            $sub  = $d[$i-1,$j-1] + $cost
            $d[$i,$j] = [Math]::Min($del, [Math]::Min($ins, $sub))
        }
    }
    return $d[$m,$n]
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Magenta
Write-Host " STEP 2: FIX NAME MISMATCHES" -ForegroundColor Magenta
Write-Host "=========================================" -ForegroundColor Magenta

# ── 1. Load all active employees ────────────────────────────────────────────
$empRows = Invoke-Rows "SELECT FullName FROM Employees WHERE IsActive = 1 AND FullName IS NOT NULL"
$empNames = @($empRows | ForEach-Object { $_.FullName })
Write-Host ("Active employees loaded: {0}" -f $empNames.Count) -ForegroundColor Cyan

# ── 2. Collect all unresolved names from the three tables ───────────────────
Write-Host ""
Write-Host "--- Loading unresolved names ---" -ForegroundColor Yellow

$unresolvedWAA = Invoke-Rows @"
SELECT DISTINCT EmployeeName FROM WicAgentAssignments
WHERE NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = WicAgentAssignments.EmployeeName)
ORDER BY EmployeeName
"@
$unresolvedARC = Invoke-Rows @"
SELECT DISTINCT EmployeeName FROM AgentReachableCities
WHERE EmployeeName IS NOT NULL AND EmployeeName <> ''
  AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = AgentReachableCities.EmployeeName)
ORDER BY EmployeeName
"@
$unresolvedWP_PA = Invoke-Rows @"
SELECT DISTINCT PrimaryAgent AS EmployeeName FROM WicPipeline
WHERE PrimaryAgent IS NOT NULL AND PrimaryAgent <> ''
  AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = WicPipeline.PrimaryAgent)
"@
$unresolvedWP_BA = Invoke-Rows @"
SELECT DISTINCT BackupAgent AS EmployeeName FROM WicPipeline
WHERE BackupAgent IS NOT NULL AND BackupAgent <> ''
  AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.FullName = WicPipeline.BackupAgent)
"@

# Combine all distinct unresolved names with source tags
$allUnresolved = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[string]]]::new()
foreach ($r in $unresolvedWAA)  { if (-not $allUnresolved.ContainsKey($r.EmployeeName)) { $allUnresolved[$r.EmployeeName] = [System.Collections.Generic.List[string]]::new() }; [void]$allUnresolved[$r.EmployeeName].Add("WicAgentAssignments") }
foreach ($r in $unresolvedARC)  { if (-not $allUnresolved.ContainsKey($r.EmployeeName)) { $allUnresolved[$r.EmployeeName] = [System.Collections.Generic.List[string]]::new() }; [void]$allUnresolved[$r.EmployeeName].Add("AgentReachableCities") }
foreach ($r in $unresolvedWP_PA){ if (-not $allUnresolved.ContainsKey($r.EmployeeName)) { $allUnresolved[$r.EmployeeName] = [System.Collections.Generic.List[string]]::new() }; [void]$allUnresolved[$r.EmployeeName].Add("WicPipeline.PrimaryAgent") }
foreach ($r in $unresolvedWP_BA){ if (-not $allUnresolved.ContainsKey($r.EmployeeName)) { $allUnresolved[$r.EmployeeName] = [System.Collections.Generic.List[string]]::new() }; [void]$allUnresolved[$r.EmployeeName].Add("WicPipeline.BackupAgent") }

Write-Host ("Distinct unresolved names: {0}" -f $allUnresolved.Count) -ForegroundColor White
Write-Host ("  WicAgentAssignments : {0}" -f $unresolvedWAA.Count)
Write-Host ("  AgentReachableCities: {0}" -f $unresolvedARC.Count)
Write-Host ("  WicPipeline (Primary): {0}" -f $unresolvedWP_PA.Count)
Write-Host ("  WicPipeline (Backup) : {0}" -f $unresolvedWP_BA.Count)

# ── 3. Match each unresolved name to an active employee ─────────────────────
Write-Host ""
Write-Host "--- Matching ---" -ForegroundColor Yellow

$groupA = @()  # resolved matches to auto-apply
$groupB = @()  # no match

foreach ($unresolvedName in $allUnresolved.Keys) {
    $normUnresolved = Normalize $unresolvedName
    $sources = $allUnresolved[$unresolvedName] -join ", "

    # Try 1: normalized exact match
    $exactMatches = @($empNames | Where-Object { (Normalize $_) -eq $normUnresolved })
    if ($exactMatches.Count -eq 1) {
        $groupA += [PSCustomObject]@{
            OldName  = $unresolvedName
            NewName  = $exactMatches[0]
            Method   = "EXACT (normalized)"
            Distance = 0
            Sources  = $sources
        }
        continue
    }
    if ($exactMatches.Count -gt 1) {
        $groupB += [PSCustomObject]@{
            Name    = $unresolvedName
            Reason  = "AMBIGUOUS: normalized exact match returned $($exactMatches.Count) employees: $($exactMatches -join ', ')"
            Sources = $sources
        }
        continue
    }

    # Try 2: fuzzy match via edit distance on normalized forms (distance <= 2)
    $candidates = @()
    foreach ($empName in $empNames) {
        $dist = Edit-Distance $normUnresolved (Normalize $empName)
        if ($dist -le 2) {
            $candidates += [PSCustomObject]@{ Name = $empName; Dist = $dist }
        }
    }
    $candidates = @($candidates | Sort-Object Dist)

    if ($candidates.Count -eq 1) {
        $groupA += [PSCustomObject]@{
            OldName  = $unresolvedName
            NewName  = $candidates[0].Name
            Method   = "FUZZY (edit-dist=$($candidates[0].Dist))"
            Distance = $candidates[0].Dist
            Sources  = $sources
        }
    } elseif ($candidates.Count -gt 1) {
        # Multiple fuzzy candidates — take best if it has clear gap, else list as ambiguous
        $best = $candidates[0].Dist
        $bestCandidates = @($candidates | Where-Object { $_.Dist -eq $best })
        if ($bestCandidates.Count -eq 1 -and $best -le 1) {
            $groupA += [PSCustomObject]@{
                OldName  = $unresolvedName
                NewName  = $bestCandidates[0].Name
                Method   = "FUZZY-UNIQUE (edit-dist=$best, next=$($candidates[1].Dist))"
                Distance = $best
                Sources  = $sources
            }
        } else {
            $topList = ($candidates | Select-Object -First 5 | ForEach-Object { "$($_.Name) (d=$($_.Dist))" }) -join "; "
            $groupB += [PSCustomObject]@{
                Name    = $unresolvedName
                Reason  = "AMBIGUOUS-FUZZY: top candidates: $topList"
                Sources = $sources
            }
        }
    } else {
        $groupB += [PSCustomObject]@{
            Name    = $unresolvedName
            Reason  = "NO MATCH: no active employee within edit distance 2"
            Sources = $sources
        }
    }
}

# ── 4. Report proposed changes ───────────────────────────────────────────────
Write-Host ""
Write-Host "--- Group (a): MATCHED - will auto-update ---" -ForegroundColor Green
foreach ($m in $groupA | Sort-Object OldName) {
    Write-Host ("  [{0}] '{1}'  ->  '{2}'  (sources: {3})" -f $m.Method, $m.OldName, $m.NewName, $m.Sources)
}

Write-Host ""
Write-Host "--- Group (b): NO MATCH - listed only, NOT changed ---" -ForegroundColor Red
foreach ($m in $groupB | Sort-Object Name) {
    Write-Host ("  '{0}' | {1} | sources: {2}" -f $m.Name, $m.Reason, $m.Sources)
}

if ($groupA.Count -eq 0) {
    Write-Host ""
    Write-Host "No matches found - nothing to update." -ForegroundColor Yellow
    exit 0
}

# ── 5. Apply group (a) in one transaction ────────────────────────────────────
Write-Host ""
Write-Host "--- Applying {0} renames in one transaction ---" -f $groupA.Count -ForegroundColor Cyan

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$tx = $null
$totalUpdated = 0

try {
    $xaCmd = $conn.CreateCommand()
    $xaCmd.CommandText = "SET XACT_ABORT ON"
    [void]$xaCmd.ExecuteNonQuery()
    $tx = $conn.BeginTransaction()

    foreach ($m in $groupA) {
        $old = $m.OldName
        $new = $m.NewName

        $rWAA = [int](Exec-Tx "UPDATE WicAgentAssignments SET EmployeeName=@new WHERE EmployeeName=@old" @{ new=$new; old=$old } $conn $tx)
        $rARC = [int](Exec-Tx "UPDATE AgentReachableCities SET EmployeeName=@new WHERE EmployeeName=@old" @{ new=$new; old=$old } $conn $tx)
        $rWP1 = [int](Exec-Tx "UPDATE WicPipeline SET PrimaryAgent=@new WHERE PrimaryAgent=@old" @{ new=$new; old=$old } $conn $tx)
        $rWP2 = [int](Exec-Tx "UPDATE WicPipeline SET BackupAgent=@new WHERE BackupAgent=@old"  @{ new=$new; old=$old } $conn $tx)

        $rowsChanged = $rWAA + $rARC + $rWP1 + $rWP2
        $totalUpdated += $rowsChanged
        Write-Host ("  OK  '{0}' -> '{1}' [{2}]  WAA={3} ARC={4} WP.PA={5} WP.BA={6}" -f $old, $new, $m.Method, $rWAA, $rARC, $rWP1, $rWP2) -ForegroundColor Green
    }

    $tx.Commit()
    Write-Host ""
    Write-Host ("Transaction committed. Total rows renamed: {0}" -f $totalUpdated) -ForegroundColor Green

} catch {
    if ($null -ne $tx) { try { $tx.Rollback() } catch {} }
    Write-Host ""
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host "Transaction rolled back." -ForegroundColor Red
    throw
} finally {
    $conn.Close()
}

# ── 6. Verify: confirm zero old names remain in each table ───────────────────
Write-Host ""
Write-Host "--- Verify: old names still present? ---" -ForegroundColor Cyan
$errCount = 0
foreach ($m in $groupA) {
    $old = $m.OldName
    $stale = @(
        Invoke-Rows "SELECT COUNT(*) AS C FROM WicAgentAssignments WHERE EmployeeName=@n" @{ n=$old }
        Invoke-Rows "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName=@n" @{ n=$old }
        Invoke-Rows "SELECT COUNT(*) AS C FROM WicPipeline WHERE PrimaryAgent=@n OR BackupAgent=@n" @{ n=$old }
    )
    $total = ($stale | ForEach-Object { [int]$_.C }) | Measure-Object -Sum | Select-Object -ExpandProperty Sum
    if ($total -gt 0) {
        Write-Host ("  FAIL: '{0}' still has {1} stale row(s)" -f $old, $total) -ForegroundColor Red
        $errCount++
    } else {
        Write-Host ("  OK: '{0}' - 0 stale rows" -f $old) -ForegroundColor Green
    }
}

Write-Host ""
if ($errCount -eq 0) {
    Write-Host "All renames verified. DONE." -ForegroundColor Green
} else {
    Write-Host ("$errCount rename(s) failed verification." ) -ForegroundColor Red
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Group (b) summary - these need your decision:" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
foreach ($m in $groupB | Sort-Object Name) {
    Write-Host ("  '{0}'" -f $m.Name) -ForegroundColor Yellow
    Write-Host ("    Reason : {0}" -f $m.Reason)
    Write-Host ("    Sources: {0}" -f $m.Sources)
}
Write-Host ""
Write-Host "Step 2 complete." -ForegroundColor Cyan
