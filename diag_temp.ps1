# WIC Diagnostic - READ ONLY - no edits to DB or code
# ASCII-only script code; data values may contain umlauts
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Continue"
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function runQ([string]$sql) {
    try {
        $cn = New-Object System.Data.SqlClient.SqlConnection($cs)
        $cm = New-Object System.Data.SqlClient.SqlCommand($sql, $cn)
        $cm.CommandTimeout = 30
        $ad = New-Object System.Data.SqlClient.SqlDataAdapter($cm)
        $dt = New-Object System.Data.DataTable
        $cn.Open()
        $ad.Fill($dt) | Out-Null
        $cn.Close()
        return $dt
    } catch {
        Write-Host "  SQL ERROR: $_"
        return $null
    }
}

function sep([string]$title) {
    Write-Host ""
    Write-Host ("=" * 72)
    Write-Host $title
    Write-Host ("=" * 72)
}

function dbVal($row, [string]$col) {
    $v = $row[$col]
    if ($v -eq $null -or $v -is [System.DBNull]) { return "-" }
    return $v.ToString()
}

function tableExists([string]$tbl) {
    $dt = runQ "SELECT COUNT(1) AS c FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='$tbl'"
    if ($dt -eq $null -or $dt.Rows.Count -eq 0) { return $false }
    return ([int]$dt.Rows[0]["c"]) -gt 0
}

function printCols($dt) {
    if ($dt -eq $null -or $dt.Rows.Count -eq 0) {
        Write-Host "  (table not found or no columns returned)"
        return
    }
    Write-Host ("{0,-40} {1,-20} {2,-12} {3}" -f "COLUMN_NAME","DATA_TYPE","MAX_LEN","IS_NULLABLE")
    Write-Host ("-" * 72)
    foreach ($row in $dt.Rows) {
        Write-Host ("{0,-40} {1,-20} {2,-12} {3}" -f `
            (dbVal $row "COLUMN_NAME"), `
            (dbVal $row "DATA_TYPE"), `
            (dbVal $row "CHARACTER_MAXIMUM_LENGTH"), `
            (dbVal $row "IS_NULLABLE"))
    }
}

function checkCols($dt, [string[]]$names) {
    $existing = @()
    if ($dt -ne $null) {
        foreach ($row in $dt.Rows) { $existing += $row["COLUMN_NAME"].ToString() }
    }
    Write-Host ""
    Write-Host "Column presence check:"
    foreach ($n in $names) {
        $yn = if ($existing -contains $n) { "YES" } else { "NO" }
        Write-Host ("  {0,-30} {1}" -f $n, $yn)
    }
}

# ============================================================
sep "1) EMPLOYEES - COLUMN SCHEMA"
# ============================================================
$empColsDt = runQ "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' ORDER BY ORDINAL_POSITION"
printCols $empColsDt
checkCols $empColsDt @("PrimaryKid","SecondaryKid","InfosysEmail","EonEmail","HasCar","GroupRegion")

# ============================================================
sep "2) WICLOCATIONS - COLUMN SCHEMA"
# ============================================================
if (tableExists "WicLocations") {
    $wicColsDt = runQ "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WicLocations' ORDER BY ORDINAL_POSITION"
    printCols $wicColsDt
    checkCols $wicColsDt @("Comment","OpeningDay","DisplayName","LocationCode","LocationCodeLegacy","Coordinates","Bundesland")
} else {
    Write-Host "  TABLE WicLocations does NOT exist."
}

# ============================================================
sep "3) WICAGENTASSIGNMENTS - COLUMN SCHEMA + DISTINCT AssignmentType"
# ============================================================
if (tableExists "WicAgentAssignments") {
    $waaColsDt = runQ "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WicAgentAssignments' ORDER BY ORDINAL_POSITION"
    printCols $waaColsDt
    Write-Host ""
    Write-Host "DISTINCT AssignmentType values:"
    $dt2 = runQ "SELECT DISTINCT AssignmentType FROM WicAgentAssignments ORDER BY AssignmentType"
    if ($dt2 -ne $null) {
        foreach ($row in $dt2.Rows) { Write-Host ("  '" + (dbVal $row "AssignmentType") + "'") }
        if ($dt2.Rows.Count -eq 0) { Write-Host "  (table is empty)" }
    }
} else {
    Write-Host "  TABLE WicAgentAssignments does NOT exist."
}

# ============================================================
sep "4) TABLE AgentReachableCities EXISTS?"
# ============================================================
if (tableExists "AgentReachableCities") {
    Write-Host "AgentReachableCities : YES"
} else {
    Write-Host "AgentReachableCities : NO"
}

# ============================================================
sep "5) ROW COUNTS"
# ============================================================
$dt = runQ "SELECT COUNT(*) AS n FROM Employees"
if ($dt -ne $null) { Write-Host ("Employees total            : " + $dt.Rows[0]["n"]) }

$dt = runQ "SELECT COUNT(*) AS n FROM Employees WHERE IsActive=1"
if ($dt -ne $null) { Write-Host ("Employees WHERE IsActive=1  : " + $dt.Rows[0]["n"]) }

if (tableExists "WicLocations") {
    $dt = runQ "SELECT COUNT(*) AS n FROM WicLocations"
    if ($dt -ne $null) { Write-Host ("WicLocations total         : " + $dt.Rows[0]["n"]) }
} else {
    Write-Host "WicLocations total         : (table not found)"
}

if (tableExists "WicAgentAssignments") {
    $dt = runQ "SELECT COUNT(*) AS n FROM WicAgentAssignments"
    if ($dt -ne $null) { Write-Host ("WicAgentAssignments total  : " + $dt.Rows[0]["n"]) }
} else {
    Write-Host "WicAgentAssignments total  : (table not found)"
}

# ============================================================
sep "6) FULL EMPLOYEES DUMP (sorted by FullName)"
# ============================================================
$empDt = runQ "SELECT EmployeeId, FullName, IsActive FROM Employees ORDER BY FullName"
if ($empDt -ne $null) {
    Write-Host ("{0,-15} {1,-50} {2}" -f "EmployeeId","FullName","IsActive")
    Write-Host ("-" * 72)
    foreach ($row in $empDt.Rows) {
        Write-Host ("{0,-15} {1,-50} {2}" -f `
            (dbVal $row "EmployeeId"), `
            (dbVal $row "FullName"), `
            (dbVal $row "IsActive"))
    }
    Write-Host ("Total rows: " + $empDt.Rows.Count)
} else {
    Write-Host "  (query failed)"
}

# ============================================================
sep "7) FULL WICLOCATIONS DUMP (sorted by DisplayName)"
# ============================================================
if (tableExists "WicLocations") {
    $wicDt = runQ "SELECT DisplayName, LocationCodeLegacy, Bundesland FROM WicLocations ORDER BY DisplayName"
    if ($wicDt -ne $null) {
        Write-Host ("{0,-42} {1,-22} {2}" -f "DisplayName","LocationCodeLegacy","Bundesland")
        Write-Host ("-" * 72)
        foreach ($row in $wicDt.Rows) {
            Write-Host ("{0,-42} {1,-22} {2}" -f `
                (dbVal $row "DisplayName"), `
                (dbVal $row "LocationCodeLegacy"), `
                (dbVal $row "Bundesland"))
        }
        Write-Host ("Total rows: " + $wicDt.Rows.Count)
    }
} else {
    Write-Host "  TABLE WicLocations does NOT exist."
}

# ============================================================
sep "8) AGENT NAME MATCHING (70 agents vs Employees.FullName)"
# ============================================================
$agentFile = "C:\GSDDashboard\diag_agents.txt"
$agents = Get-Content $agentFile -Encoding UTF8 | Where-Object { $_.Trim() -ne "" }

$allEmpNames = @()
if ($empDt -ne $null) {
    foreach ($row in $empDt.Rows) { $allEmpNames += $row["FullName"].ToString() }
}

$matched   = [System.Collections.Generic.List[string]]::new()
$unmatched = [System.Collections.Generic.List[string]]::new()

foreach ($agent in $agents) {
    $a = $agent.Trim()
    $aLow = $a.ToLowerInvariant()
    $hit = $allEmpNames | Where-Object { $_.Trim().ToLowerInvariant() -eq $aLow }
    if ($hit) {
        $matched.Add($a)
    } else {
        $unmatched.Add($a)
    }
}

Write-Host ("MATCHED: " + $matched.Count + " / " + $agents.Count)
Write-Host ""
Write-Host "--- MATCHED LIST ---"
foreach ($m in $matched) { Write-Host ("  OK   " + $m) }

Write-Host ""
Write-Host ("--- NOT MATCHED: " + $unmatched.Count + " ---")
foreach ($u in $unmatched) {
    Write-Host ("  MISS  " + $u)
    $uLow   = $u.ToLowerInvariant()
    $uParts = $uLow -split "\s+"
    $uFirst = $uParts[0]
    $uLast  = $uParts[$uParts.Length - 1]

    # Try first name match
    $sugg = $allEmpNames | Where-Object { $_.ToLowerInvariant().StartsWith($uFirst) }
    if (-not $sugg) {
        # Try last name match
        $sugg = $allEmpNames | Where-Object { $_.ToLowerInvariant().EndsWith($uLast) }
    }
    if (-not $sugg) {
        # Try any word overlap
        foreach ($part in $uParts) {
            if ($part.Length -gt 3) {
                $sugg = $allEmpNames | Where-Object { $_.ToLowerInvariant().Contains($part) }
                if ($sugg) { break }
            }
        }
    }
    if ($sugg) {
        foreach ($s in $sugg) { Write-Host ("        -> " + $s) }
    } else {
        Write-Host "        -> (no close match found in DB)"
    }
}

# ============================================================
sep "9) WIC CITY MATCHING (44 WICs vs WicLocations.DisplayName)"
# ============================================================
$wicFile = "C:\GSDDashboard\diag_wics.txt"
$cities  = Get-Content $wicFile -Encoding UTF8 | Where-Object { $_.Trim() -ne "" }

$allWicNames = @()
if ((tableExists "WicLocations")) {
    $wicNmDt = runQ "SELECT DisplayName FROM WicLocations ORDER BY DisplayName"
    if ($wicNmDt -ne $null) {
        foreach ($row in $wicNmDt.Rows) { $allWicNames += $row["DisplayName"].ToString() }
    }
}

$matched2   = [System.Collections.Generic.List[string]]::new()
$unmatched2 = [System.Collections.Generic.List[string]]::new()

foreach ($city in $cities) {
    $c    = $city.Trim()
    $cLow = $c.ToLowerInvariant()
    $hit  = $allWicNames | Where-Object { $_.Trim().ToLowerInvariant() -eq $cLow }
    if ($hit) {
        $matched2.Add($c)
    } else {
        $unmatched2.Add($c)
    }
}

Write-Host ("MATCHED: " + $matched2.Count + " / " + $cities.Count)
Write-Host ""
Write-Host "--- MATCHED LIST ---"
foreach ($m in $matched2) { Write-Host ("  OK   " + $m) }

Write-Host ""
Write-Host ("--- NOT MATCHED: " + $unmatched2.Count + " ---")
foreach ($u in $unmatched2) {
    Write-Host ("  MISS  " + $u)
    $uLow   = $u.ToLowerInvariant()
    $uParts = $uLow -split "[\s\-]+" | Where-Object { $_.Length -gt 2 }

    $sugg = @()
    foreach ($part in $uParts) {
        $s = $allWicNames | Where-Object { $_.ToLowerInvariant().Contains($part) }
        if ($s) { $sugg += $s }
    }
    $sugg = $sugg | Select-Object -Unique

    if ($sugg) {
        foreach ($s in $sugg) { Write-Host ("        -> " + $s) }
    } else {
        Write-Host "        -> (no close match found in DB)"
    }
}

Write-Host ""
Write-Host "=== DIAGNOSTIC COMPLETE ==="
Write-Host "Temp files to delete: diag_temp.ps1, diag_agents.txt, diag_wics.txt"
