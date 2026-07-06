# PS1_Step2b_FixNames.ps1
# Explicit renames only - no fuzzy matching.
# Step 1: verify each target name exists in Employees via LIKE.
# Step 2: rename in WicAgentAssignments, AgentReachableCities, WicPipeline in one transaction.
# Orphaned assignments (no matching employee) are listed at the end - NOT touched.

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
    $cmd.CommandText  = $sql
    foreach ($kv in $params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    return $cmd.ExecuteNonQuery()
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Magenta
Write-Host " STEP 2b: EXPLICIT RENAMES" -ForegroundColor Magenta
Write-Host "==========================================" -ForegroundColor Magenta

# ═══════════════════════════════════════════════════════
# STEP 1: Verify target names exist in Employees
# Each search: print all matches, then decide.
# ═══════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Step 1: verify target names in Employees ---" -ForegroundColor Cyan

# --- Search 1: "Aman Kedo" -> look for Amani Kedo variant ---
Write-Host ""
Write-Host "  Search 1: Employees LIKE '%Aman%Kedo%' (looking for Amani Kedo stored form)" -ForegroundColor Yellow
$search1 = Invoke-Rows "SELECT FullName FROM Employees WHERE IsActive = 1 AND FullName LIKE '%Aman%Kedo%' ORDER BY FullName"
if ($search1.Count -eq 0) {
    Write-Host "    RESULT: 0 rows found. Also trying inactive..." -ForegroundColor Red
    $search1all = Invoke-Rows "SELECT FullName, IsActive FROM Employees WHERE FullName LIKE '%Aman%Kedo%' ORDER BY FullName"
    if ($search1all.Count -eq 0) {
        Write-Host "    RESULT: 0 rows found even including inactive. STOP." -ForegroundColor Red
        exit 1
    }
    foreach ($r in $search1all) { Write-Host ("    Found (inactive): '{0}' IsActive={1}" -f $r.FullName, $r.IsActive) -ForegroundColor Yellow }
} else {
    foreach ($r in $search1) { Write-Host ("    Found: '{0}'" -f $r.FullName) -ForegroundColor Green }
}
if ($search1.Count -ne 1) {
    Write-Host ("    STOP: expected exactly 1 active match, got {0}. Clarify and rerun." -f $search1.Count) -ForegroundColor Red
    exit 1
}
$realAman = $search1[0].FullName
Write-Host ("    -> Will rename 'Aman Kedo' to '{0}'" -f $realAman) -ForegroundColor Green

# --- Search 2: Kumlehn variant (Kai Eric vs Kai Erik) ---
Write-Host ""
Write-Host "  Search 2: Employees LIKE '%Kumlehn%' (looking for Kai Kumlehn stored form)" -ForegroundColor Yellow
$search2 = Invoke-Rows "SELECT FullName FROM Employees WHERE IsActive = 1 AND FullName LIKE '%Kumlehn%' ORDER BY FullName"
if ($search2.Count -eq 0) {
    Write-Host "    RESULT: 0 rows found. Also trying inactive..." -ForegroundColor Red
    $search2all = Invoke-Rows "SELECT FullName, IsActive FROM Employees WHERE FullName LIKE '%Kumlehn%' ORDER BY FullName"
    if ($search2all.Count -eq 0) {
        Write-Host "    RESULT: 0 rows found even including inactive. STOP." -ForegroundColor Red
        exit 1
    }
    foreach ($r in $search2all) { Write-Host ("    Found (inactive): '{0}' IsActive={1}" -f $r.FullName, $r.IsActive) -ForegroundColor Yellow }
} else {
    foreach ($r in $search2) { Write-Host ("    Found: '{0}'" -f $r.FullName) -ForegroundColor Green }
}
if ($search2.Count -ne 1) {
    Write-Host ("    STOP: expected exactly 1 active match, got {0}. Clarify and rerun." -f $search2.Count) -ForegroundColor Red
    exit 1
}
$realKumlehn = $search2[0].FullName
Write-Host ("    -> Will rename 'Kai Eric Kumlehn' to '{0}'" -f $realKumlehn) -ForegroundColor Green

# --- Search 3: Elliot van Staveren Kust... ---
Write-Host ""
Write-Host "  Search 3: Employees LIKE '%van Staveren%Kust%' (looking for Elliot van Staveren Kust... stored form)" -ForegroundColor Yellow
$search3 = Invoke-Rows "SELECT FullName FROM Employees WHERE IsActive = 1 AND FullName LIKE '%van Staveren%Kust%' ORDER BY FullName"
if ($search3.Count -eq 0) {
    Write-Host "    0 rows for '%van Staveren%Kust%'. Trying broader '%Staveren%'..." -ForegroundColor Yellow
    $search3b = Invoke-Rows "SELECT FullName FROM Employees WHERE IsActive = 1 AND FullName LIKE '%Staveren%' ORDER BY FullName"
    if ($search3b.Count -eq 0) {
        Write-Host "    RESULT: 0 rows. Also trying inactive '%Staveren%'..." -ForegroundColor Red
        $search3all = Invoke-Rows "SELECT FullName, IsActive FROM Employees WHERE FullName LIKE '%Staveren%' ORDER BY FullName"
        if ($search3all.Count -eq 0) {
            Write-Host "    RESULT: 0 rows even including inactive. STOP." -ForegroundColor Red
            exit 1
        }
        foreach ($r in $search3all) { Write-Host ("    Found (inactive): '{0}' IsActive={1}" -f $r.FullName, $r.IsActive) -ForegroundColor Yellow }
    } else {
        $search3 = $search3b
        foreach ($r in $search3) { Write-Host ("    Found (broad): '{0}'" -f $r.FullName) -ForegroundColor Green }
    }
} else {
    foreach ($r in $search3) { Write-Host ("    Found: '{0}'" -f $r.FullName) -ForegroundColor Green }
}
if ($search3.Count -ne 1) {
    Write-Host ("    STOP: expected exactly 1 active match, got {0}. Clarify and rerun." -f $search3.Count) -ForegroundColor Red
    exit 1
}
$realElliot = $search3[0].FullName
Write-Host ("    -> Will rename 'Elliot van Staveren Kuster' to '{0}'" -f $realElliot) -ForegroundColor Green

# ═══════════════════════════════════════════════════════
# STEP 2: Check whether the "old" name is actually different from the found name.
#         If they are already identical, skip that rename.
# ═══════════════════════════════════════════════════════
Write-Host ""
Write-Host "--- Step 2: build rename map ---" -ForegroundColor Cyan

$renameMap = @()

if ($realAman -ne 'Aman Kedo') {
    $renameMap += [PSCustomObject]@{ Old = 'Aman Kedo'; New = $realAman }
    Write-Host ("  RENAME: 'Aman Kedo' -> '{0}'" -f $realAman)
} else {
    Write-Host "  SKIP: 'Aman Kedo' already matches stored name - no rename needed."
}

if ($realKumlehn -ne 'Kai Eric Kumlehn') {
    $renameMap += [PSCustomObject]@{ Old = 'Kai Eric Kumlehn'; New = $realKumlehn }
    Write-Host ("  RENAME: 'Kai Eric Kumlehn' -> '{0}'" -f $realKumlehn)
} else {
    Write-Host "  SKIP: 'Kai Eric Kumlehn' already matches stored name - no rename needed."
}

if ($realElliot -ne 'Elliot van Staveren Kuster') {
    $renameMap += [PSCustomObject]@{ Old = 'Elliot van Staveren Kuster'; New = $realElliot }
    Write-Host ("  RENAME: 'Elliot van Staveren Kuster' -> '{0}'" -f $realElliot)
} else {
    Write-Host "  SKIP: 'Elliot van Staveren Kuster' already matches stored name - no rename needed."
}

if ($renameMap.Count -eq 0) {
    Write-Host ""
    Write-Host "Nothing to rename - all stored names already match. DONE." -ForegroundColor Green
} else {

    # ═══════════════════════════════════════════════════════
    # STEP 3: Execute renames in one transaction
    # ═══════════════════════════════════════════════════════
    Write-Host ""
    Write-Host ("--- Step 3: executing {0} rename(s) in one transaction ---" -f $renameMap.Count) -ForegroundColor Cyan

    $conn = New-Object System.Data.SqlClient.SqlConnection($cs)
    $conn.Open()
    $tx = $null

    try {
        $xaCmd = $conn.CreateCommand()
        $xaCmd.CommandText = "SET XACT_ABORT ON"
        [void]$xaCmd.ExecuteNonQuery()
        $tx = $conn.BeginTransaction()

        foreach ($entry in $renameMap) {
            $old = $entry.Old
            $new = $entry.New

            $rWAA = [int](Exec-Tx `
                "UPDATE WicAgentAssignments SET EmployeeName = @new WHERE EmployeeName = @old" `
                @{ new = $new; old = $old } $conn $tx)

            $rARC = [int](Exec-Tx `
                "UPDATE AgentReachableCities SET EmployeeName = @new WHERE EmployeeName = @old" `
                @{ new = $new; old = $old } $conn $tx)

            $rWP1 = [int](Exec-Tx `
                "UPDATE WicPipeline SET PrimaryAgent = @new WHERE PrimaryAgent = @old" `
                @{ new = $new; old = $old } $conn $tx)

            $rWP2 = [int](Exec-Tx `
                "UPDATE WicPipeline SET BackupAgent = @new WHERE BackupAgent = @old" `
                @{ new = $new; old = $old } $conn $tx)

            Write-Host ("  '{0}' -> '{1}'" -f $old, $new)
            Write-Host ("    WicAgentAssignments : {0} row(s)" -f $rWAA)
            Write-Host ("    AgentReachableCities: {0} row(s)" -f $rARC)
            Write-Host ("    WicPipeline.Primary : {0} row(s)" -f $rWP1)
            Write-Host ("    WicPipeline.Backup  : {0} row(s)" -f $rWP2)
        }

        $tx.Commit()
        Write-Host ""
        Write-Host "Transaction committed." -ForegroundColor Green

    } catch {
        if ($null -ne $tx) { try { $tx.Rollback() } catch {} }
        Write-Host ""
        Write-Host "ERROR: $_" -ForegroundColor Red
        Write-Host "Transaction rolled back. No data written." -ForegroundColor Red
        throw
    } finally {
        $conn.Close()
    }

    # ═══════════════════════════════════════════════════════
    # STEP 4: Verify - confirm old names are gone
    # ═══════════════════════════════════════════════════════
    Write-Host ""
    Write-Host "--- Step 4: verify old names no longer present ---" -ForegroundColor Cyan
    $errCount = 0
    foreach ($entry in $renameMap) {
        $old = $entry.Old
        $staleWAA = Invoke-Rows "SELECT COUNT(*) AS C FROM WicAgentAssignments  WHERE EmployeeName  = @n" @{ n = $old }
        $staleARC = Invoke-Rows "SELECT COUNT(*) AS C FROM AgentReachableCities WHERE EmployeeName  = @n" @{ n = $old }
        $staleWP  = Invoke-Rows "SELECT COUNT(*) AS C FROM WicPipeline WHERE PrimaryAgent = @n OR BackupAgent = @n" @{ n = $old }
        $total = [int]$staleWAA[0].C + [int]$staleARC[0].C + [int]$staleWP[0].C
        if ($total -gt 0) {
            Write-Host ("  FAIL: '{0}' still present in {1} row(s)" -f $old, $total) -ForegroundColor Red
            $errCount++
        } else {
            Write-Host ("  OK  : '{0}' - 0 stale rows" -f $old) -ForegroundColor Green
        }
    }
    if ($errCount -eq 0) {
        Write-Host ""
        Write-Host "All renames verified." -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host ("$errCount rename(s) failed verification." ) -ForegroundColor Red
    }
}

# ═══════════════════════════════════════════════════════
# FINAL: List orphaned assignments - NOT touched
# ═══════════════════════════════════════════════════════
Write-Host ""
Write-Host "==========================================" -ForegroundColor Yellow
Write-Host " ORPHANED ASSIGNMENTS - no employee - awaiting decision" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

$orphanNames = @(
    'Christos Kyrillidis'
    'Elias Erdem'
    'Patrick Henschel'
    'Karlo Coric'
    'Maik Kopperschmidt'
)

foreach ($name in $orphanNames) {
    $waaRows = Invoke-Rows "SELECT Id, LocationCode, AssignmentType, IsActive FROM WicAgentAssignments WHERE EmployeeName = @n" @{ n = $name }
    $arcRows = Invoke-Rows "SELECT Id, City FROM AgentReachableCities WHERE EmployeeName = @n" @{ n = $name }
    $wpRows  = Invoke-Rows "SELECT Id, LocationCode, PipelineDate FROM WicPipeline WHERE PrimaryAgent = @n OR BackupAgent = @n" @{ n = $name }
    $total   = $waaRows.Count + $arcRows.Count + $wpRows.Count

    if ($total -eq 0) {
        Write-Host ("  '{0}': 0 rows in any table (already clean or never present)" -f $name) -ForegroundColor DarkGray
    } else {
        Write-Host ("  '{0}': {1} row(s) total" -f $name, $total) -ForegroundColor Yellow
        foreach ($r in $waaRows) { Write-Host ("    WicAgentAssignments : Id={0} LocationCode={1} Type={2} IsActive={3}" -f $r.Id, $r.LocationCode, $r.AssignmentType, $r.IsActive) }
        foreach ($r in $arcRows) { Write-Host ("    AgentReachableCities: Id={0} City={1}" -f $r.Id, $r.City) }
        foreach ($r in $wpRows)  { Write-Host ("    WicPipeline         : Id={0} LocationCode={1} Date={2}" -f $r.Id, $r.LocationCode, $r.PipelineDate) }
    }
}

Write-Host ""
Write-Host "Step 2b complete." -ForegroundColor Cyan
