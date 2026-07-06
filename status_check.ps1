# status_check.ps1
# Read-only status check. SELECT/COUNT only - nothing is modified.
#
# Run with: pwsh -File C:\GSDDashboard\status_check.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Add-Type -AssemblyName "System.Data"

$Conn = New-Object System.Data.SqlClient.SqlConnection($ConnString)
$Conn.Open()

function Invoke-Scalar([string]$Sql, [hashtable]$Params = @{}) {
    $cmd = $script:Conn.CreateCommand()
    $cmd.CommandText = $Sql
    foreach ($kv in $Params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    $raw = $cmd.ExecuteScalar()
    $v = if ($raw -is [System.DBNull] -or $null -eq $raw) { $null } else { $raw }
    return $v
}

function Invoke-Rows([string]$Sql, [hashtable]$Params = @{}) {
    $cmd = $script:Conn.CreateCommand()
    $cmd.CommandText = $Sql
    foreach ($kv in $Params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    $reader = $cmd.ExecuteReader()
    $rows = @()
    while ($reader.Read()) {
        $row = @{}
        for ($i = 0; $i -lt $reader.FieldCount; $i++) {
            $raw = $reader.GetValue($i)
            $row[$reader.GetName($i)] = if ($raw -is [System.DBNull]) { $null } else { $raw }
        }
        $rows += [PSCustomObject]$row
    }
    $reader.Close()
    return $rows
}

function Count-Sql([string]$Sql, [hashtable]$Params = @{}) {
    $raw = Invoke-Scalar -Sql $Sql -Params $Params
    $n = if ($null -eq $raw) { 0 } else { [int]$raw }
    return $n
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Yellow
Write-Host "  GSD DASHBOARD -- PENDING TASKS STATUS CHECK"               -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Yellow
Write-Host ""

# ============================================================
# TASK 1: SLS DELETE - Ferenc Koreh, Tunde Szabo, Zsolt Fulop
# ============================================================

Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "  TASK 1: SLS DELETE (Ferenc Koreh / Tunde Szabo / Zsolt Fulop)" -ForegroundColor Cyan
Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
Write-Host ""

$slsTargets = @(
    [PSCustomObject]@{ Pattern = "%Koreh%";  Label = "Ferenc Koreh" },
    [PSCustomObject]@{ Pattern = "%Szabo%";  Label = "Tunde Szabo"  },
    [PSCustomObject]@{ Pattern = "%Zsabo%";  Label = "Tunde Szabo (variant)" },
    [PSCustomObject]@{ Pattern = "%Fulop%";  Label = "Zsolt Fulop"  }
)

$slsFound = @{}

foreach ($t in $slsTargets) {
    $rows = @(Invoke-Rows "SELECT EmployeeId, FullName, IsActive FROM Employees WHERE FullName LIKE @pat COLLATE Latin1_General_CI_AI" @{pat=$t.Pattern})
    foreach ($r in $rows) {
        $key = "$($r.EmployeeId)"
        if (-not $slsFound.ContainsKey($key)) {
            $slsFound[$key] = $r
            Write-Host ("  STILL PRESENT:  ID={0}  FullName='{1}'  IsActive={2}" -f $r.EmployeeId, $r.FullName, $r.IsActive) -ForegroundColor Red
        }
    }
}

if ($slsFound.Count -eq 0) {
    Write-Host "  All three names absent from Employees." -ForegroundColor Green
    Write-Host ""
    Write-Host "  TASK 1: DONE -- SLS employees deleted." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host ("  TASK 1: NOT DONE -- {0} name(s) still present in Employees." -f $slsFound.Count) -ForegroundColor Red
}

Write-Host ""

# ============================================================
# TASK 2: MASALMA UPDATE
# ============================================================

Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "  TASK 2: MASALMA UPDATE (Mohammad Al Masalma)"               -ForegroundColor Cyan
Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
Write-Host ""

$masRows = @(Invoke-Rows "SELECT EmployeeId, FullName, PrimaryKid, EonEmail FROM Employees WHERE FullName LIKE '%Al Masal%' COLLATE Latin1_General_CI_AI")

if ($masRows.Count -eq 0) {
    Write-Host "  WARNING: No row matching '%Al Masal%' found at all -- investigate." -ForegroundColor Red
    Write-Host "  TASK 2: NOT DONE (employee row missing)." -ForegroundColor Red
} else {
    foreach ($r in $masRows) {
        Write-Host ("  ID={0}  FullName='{1}'  PrimaryKid='{2}'  EonEmail='{3}'" -f `
            $r.EmployeeId, $r.FullName,
            $(if ($null -eq $r.PrimaryKid) { "(null)" } else { $r.PrimaryKid }),
            $(if ($null -eq $r.EonEmail)   { "(null)" } else { $r.EonEmail })) -ForegroundColor White
    }
    Write-Host ""

    $nameOk  = $masRows.Count -eq 1 -and "$($masRows[0].FullName)" -eq "Mohammad Al Masalma"
    $kidOk   = $masRows.Count -eq 1 -and "$($masRows[0].PrimaryKid)" -eq "M101365"
    $emailOk = $masRows.Count -eq 1 -and -not [string]::IsNullOrEmpty("$($masRows[0].EonEmail)") -and "$($masRows[0].EonEmail)" -ne ""

    $nameTag  = if ($nameOk)  { "[OK]" } else { "[NOT DONE]" }
    $kidTag   = if ($kidOk)   { "[OK]" } else { "[NOT DONE]" }
    $emailTag = if ($emailOk) { "[OK]" } else { "[NOT DONE]" }

    Write-Host ("  FullName = 'Mohammad Al Masalma'         {0}" -f $nameTag)  -ForegroundColor $(if ($nameOk)  { "Green" } else { "Red" })
    Write-Host ("  PrimaryKid = 'M101365'                   {0}" -f $kidTag)   -ForegroundColor $(if ($kidOk)   { "Green" } else { "Red" })
    Write-Host ("  EonEmail set                             {0}" -f $emailTag) -ForegroundColor $(if ($emailOk) { "Green" } else { "Red" })
    Write-Host ""

    # Check for orphaned name-keyed references under the OLD name
    $oldName    = "Mohammad Al Masalama"
    $oldWaa     = Count-Sql "SELECT COUNT(*) FROM WicAgentAssignments  WHERE EmployeeName = @n" @{n=$oldName}
    $oldArc     = Count-Sql "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName = @n" @{n=$oldName}
    $oldWpPrim  = Count-Sql "SELECT COUNT(*) FROM WicPipeline          WHERE PrimaryAgent = @n" @{n=$oldName}
    $oldWpBack  = Count-Sql "SELECT COUNT(*) FROM WicPipeline          WHERE BackupAgent  = @n" @{n=$oldName}
    $oldTrain   = Count-Sql "SELECT COUNT(*) FROM TrainingSchedule     WHERE SuggestedBy = @n OR ConfirmedBy = @n" @{n=$oldName}

    $oldTotal = $oldWaa + $oldArc + $oldWpPrim + $oldWpBack + $oldTrain

    if ($oldTotal -gt 0) {
        Write-Host ("  Old name '{0}' still referenced in:" -f $oldName) -ForegroundColor Red
        if ($oldWaa    -gt 0) { Write-Host ("    WicAgentAssignments.EmployeeName   {0,4} row(s)" -f $oldWaa)    -ForegroundColor Red }
        if ($oldArc    -gt 0) { Write-Host ("    AgentReachableCities.EmployeeName  {0,4} row(s)" -f $oldArc)    -ForegroundColor Red }
        if ($oldWpPrim -gt 0) { Write-Host ("    WicPipeline.PrimaryAgent           {0,4} row(s)" -f $oldWpPrim) -ForegroundColor Red }
        if ($oldWpBack -gt 0) { Write-Host ("    WicPipeline.BackupAgent            {0,4} row(s)" -f $oldWpBack) -ForegroundColor Red }
        if ($oldTrain  -gt 0) { Write-Host ("    TrainingSchedule SuggestBy/Confirm {0,4} row(s)" -f $oldTrain)  -ForegroundColor Red }
    } else {
        Write-Host ("  No DB rows still reference old name '{0}'." -f $oldName) -ForegroundColor Green
    }
    Write-Host ""

    # Codebase grep for 'Al Masalama' in WicCoverageImport.cs
    $importFile = "C:\GSDDashboard\Backend\Services\WicCoverageImport.cs"
    $codeHits = @()
    if (Test-Path $importFile) {
        $codeHits = @(Select-String -Path $importFile -Pattern "Al Masalama" -SimpleMatch)
    }
    if ($codeHits.Count -gt 0) {
        Write-Host ("  WicCoverageImport.cs still contains 'Al Masalama' ({0} line(s)):" -f $codeHits.Count) -ForegroundColor Red
        foreach ($h in $codeHits) {
            Write-Host ("    Line {0}: {1}" -f $h.LineNumber, $h.Line.Trim()) -ForegroundColor Red
        }
    } else {
        Write-Host "  WicCoverageImport.cs: no 'Al Masalama' found (code updated)." -ForegroundColor Green
    }
    Write-Host ""

    $task2Done = $nameOk -and $kidOk -and $emailOk -and ($oldTotal -eq 0) -and ($codeHits.Count -eq 0)
    if ($task2Done) {
        Write-Host "  TASK 2: DONE -- name, KID, email updated; no orphaned refs; code clean." -ForegroundColor Green
    } else {
        Write-Host "  TASK 2: NOT DONE -- see items above." -ForegroundColor Red
    }
}

Write-Host ""

# ============================================================
# TASK 3: BO LIST 2026-07-06
# ============================================================

Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "  TASK 3: BO LIST INSERT (ShiftDate = 2026-07-06)"            -ForegroundColor Cyan
Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
Write-Host ""

$boDate  = "2026-07-06"
$boCount = Count-Sql "SELECT COUNT(*) FROM ShiftEntries WHERE ShiftDate = @d" @{d=$boDate}

Write-Host ("  Total ShiftEntries for {0}: {1}" -f $boDate, $boCount) -ForegroundColor White
Write-Host ""

if ($boCount -gt 0) {
    $boRows = @(Invoke-Rows "SELECT e.FullName, s.ShiftType, s.ShiftStart, s.ShiftEnd, s.AgentTask FROM ShiftEntries s JOIN Employees e ON e.EmployeeId = s.EmployeeId WHERE s.ShiftDate = @d ORDER BY e.FullName" @{d=$boDate})
    Write-Host ("  Distinct employees ({0} entries):" -f $boRows.Count)
    foreach ($r in $boRows) {
        $note = if ($null -eq $r.AgentTask -or "$($r.AgentTask)" -eq "") { "" } else { "  note=$($r.AgentTask)" }
        Write-Host ("    {0,-34} {1,-12} {2}-{3}{4}" -f $r.FullName, $r.ShiftType, $r.ShiftStart, $r.ShiftEnd, $note) -ForegroundColor White
    }
    Write-Host ""
}

if ($boCount -ge 18) {
    Write-Host ("  TASK 3: DONE -- {0} BO entries present for {1}." -f $boCount, $boDate) -ForegroundColor Green
} elseif ($boCount -gt 0) {
    Write-Host ("  TASK 3: PARTIAL -- only {0} entries present (expected ~21). Investigate." -f $boCount) -ForegroundColor Yellow
} else {
    Write-Host ("  TASK 3: NOT DONE -- 0 ShiftEntries for {0}." -f $boDate) -ForegroundColor Red
}

Write-Host ""

# ============================================================
# TASK 4: SANITY -- previously deleted employees absent
# ============================================================

Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "  TASK 4: SANITY CHECK (prior deletes + total employee count)" -ForegroundColor Cyan
Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
Write-Host ""

$sanityTargets = @(
    [PSCustomObject]@{ Pattern = "%Kwasniewska%"; Label = "Teresa Kwasniewska" },
    [PSCustomObject]@{ Pattern = "%Obazee%";      Label = "Dennis Obazee"      },
    [PSCustomObject]@{ Pattern = "%Mahmoud%";     Label = "Mohamed Khaled Mahmoud" }
)

$sanityFail = $false
foreach ($t in $sanityTargets) {
    $rows = @(Invoke-Rows "SELECT EmployeeId, FullName FROM Employees WHERE FullName LIKE @pat COLLATE Latin1_General_CI_AI" @{pat=$t.Pattern})
    if ($rows.Count -eq 0) {
        Write-Host ("  ABSENT (OK):  {0}" -f $t.Label) -ForegroundColor Green
    } else {
        $sanityFail = $true
        foreach ($r in $rows) {
            Write-Host ("  STILL PRESENT (BAD):  ID={0}  FullName='{1}'" -f $r.EmployeeId, $r.FullName) -ForegroundColor Red
        }
    }
}

Write-Host ""
$totalEmp = Count-Sql "SELECT COUNT(*) FROM Employees"
Write-Host ("  Total Employees in DB: {0}" -f $totalEmp) -ForegroundColor White
Write-Host ""

if (-not $sanityFail) {
    Write-Host "  TASK 4: DONE -- all prior deletions confirmed absent." -ForegroundColor Green
} else {
    Write-Host "  TASK 4: NOT DONE -- unexpected rows still present." -ForegroundColor Red
}

$Conn.Close()

# ============================================================
# Final summary
# ============================================================

Write-Host ""
Write-Host "============================================================" -ForegroundColor Yellow
Write-Host "  END OF STATUS CHECK"                                         -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Yellow
Write-Host ""
