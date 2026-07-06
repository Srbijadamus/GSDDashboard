# sls_phase1_find.ps1
# PHASE 1: Read-only investigation for 3 SLS-departing employees.
#   Search by name (no known IDs), using multiple LIKE patterns
#   to handle spelling variants (Szabo/Zsabo, Fulop/Fueloep, Koreh).
#   Count 17 table/columns per match. Report WIC impact.
#   Nothing is deleted.
#
# Run with: pwsh -File C:\GSDDashboard\sls_phase1_find.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Add-Type -AssemblyName "System.Data"

# Each target: canonical name + array of LIKE patterns to search
$Targets = @(
    [PSCustomObject]@{
        Expected = "Ferenc Koreh"
        Patterns = @("%Koreh%")
    },
    [PSCustomObject]@{
        Expected = "Tunde Szabo"
        Patterns = @("%Szabo%", "%Zsabo%")
    },
    [PSCustomObject]@{
        Expected = "Zsolt Fulop"
        Patterns = @("%Fulop%", "%Fueloep%")
    }
)

$Conn = New-Object System.Data.SqlClient.SqlConnection($ConnString)
$Conn.Open()

# ---------------------------------------------------------------------------
# Helpers -- PS 5.1 safe: assign if-expression to variable before return;
#            iterate @($dict.Keys) copy to avoid "Collection was modified".
# ---------------------------------------------------------------------------

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

function Count-Sql([string]$Sql, [hashtable]$Params = @{}) {
    $raw = Invoke-Scalar -Sql $Sql -Params $Params
    $n = if ($null -eq $raw) { 0 } else { [int]$raw }
    return $n
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

function Get-Counts([string]$Id, [string]$Name) {
    $pat = "%$Id%"
    $c = [ordered]@{}
    $c["Employees"]                  = Count-Sql "SELECT COUNT(*) FROM Employees            WHERE EmployeeId         = @id"                   @{id=$Id}
    $c["ShiftEntries"]               = Count-Sql "SELECT COUNT(*) FROM ShiftEntries         WHERE EmployeeId         = @id"                   @{id=$Id}
    $c["WicShiftEntries"]            = Count-Sql "SELECT COUNT(*) FROM WicShiftEntries      WHERE EmployeeId         = @id"                   @{id=$Id}
    $c["SickLeaves"]                 = Count-Sql "SELECT COUNT(*) FROM SickLeaves           WHERE EmployeeId         = @id"                   @{id=$Id}
    $c["Vacations"]                  = Count-Sql "SELECT COUNT(*) FROM Vacations            WHERE EmployeeId         = @id"                   @{id=$Id}
    $c["ALBalance"]                  = Count-Sql "SELECT COUNT(*) FROM ALBalance            WHERE EmployeeId         = @id"                   @{id=$Id}
    $c["SubstitutionHistory"]        = Count-Sql "SELECT COUNT(*) FROM SubstitutionHistory  WHERE EmployeeId         = @id"                   @{id=$Id}
    $c["BreakSlots"]                 = Count-Sql "SELECT COUNT(*) FROM BreakSlots           WHERE EmployeeId         = @id"                   @{id=$Id}
    $c["VwicRotationSlots"]          = Count-Sql "SELECT COUNT(*) FROM VwicRotationSlots    WHERE EmployeeId         = @id"                   @{id=$Id}
    $c["WicAgentAssignments(name)"]  = Count-Sql "SELECT COUNT(*) FROM WicAgentAssignments  WHERE EmployeeName       = @n"                    @{n=$Name}
    $c["AgentReachableCities(id)"]   = Count-Sql "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeId        = @id"                   @{id=$Id}
    $c["AgentReachableCities(name)"] = Count-Sql "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName      = @n"                    @{n=$Name}
    $c["DailyAttendances"]           = Count-Sql "SELECT COUNT(*) FROM DailyAttendance      WHERE AssignedEmployeeId = @id"                   @{id=$Id}
    $c["WicPipeline Primary"]        = Count-Sql "SELECT COUNT(*) FROM WicPipeline          WHERE PrimaryAgent       = @n"                    @{n=$Name}
    $c["WicPipeline Backup"]         = Count-Sql "SELECT COUNT(*) FROM WicPipeline          WHERE BackupAgent        = @n"                    @{n=$Name}
    $c["TrainingSchedule AgentIds"]  = Count-Sql "SELECT COUNT(*) FROM TrainingSchedule     WHERE AgentIds LIKE @pat"                         @{pat=$pat}
    $c["TrainingSchedule SuggestBy"] = Count-Sql "SELECT COUNT(*) FROM TrainingSchedule     WHERE SuggestedBy = @n OR ConfirmedBy = @n"      @{n=$Name}
    return $c
}

# ---------------------------------------------------------------------------
# Name search: for each target try all patterns, collect unique rows
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=== SLS PHASE 1: READ-ONLY INVESTIGATION ===" -ForegroundColor Yellow
Write-Host "Nothing will be deleted." -ForegroundColor Yellow
Write-Host ""
Write-Host "--- Name Search (LIKE, COLLATE Latin1_General_CI_AI) ---" -ForegroundColor Cyan
Write-Host ""

$Located    = @()
$NotFound   = @()
$Ambiguous  = @()

foreach ($t in $Targets) {
    $allMatches   = @{}
    $patternsUsed = @()

    foreach ($pat in $t.Patterns) {
        $rows = @(Invoke-Rows "SELECT EmployeeId, FullName, PrimaryRole, IsActive FROM Employees WHERE FullName LIKE @pat COLLATE Latin1_General_CI_AI ORDER BY FullName" @{pat=$pat})
        $patternsUsed += $pat
        foreach ($r in $rows) {
            $key = "$($r.EmployeeId)"
            if (-not $allMatches.ContainsKey($key)) {
                $allMatches[$key] = $r
            }
        }
    }

    $unique = @()
    foreach ($k in @($allMatches.Keys)) { $unique += $allMatches[$k] }

    if ($unique.Count -eq 0) {
        Write-Host ("  NOT FOUND:  expected='{0}'  patterns={1}  -> 0 results" -f $t.Expected, ($patternsUsed -join ", ")) -ForegroundColor Red
        $NotFound += $t.Expected
        continue
    }

    if ($unique.Count -eq 1) {
        $emp = $unique[0]
        Write-Host ("  FOUND (1):  expected='{0}'  -> ID={1}  DB='{2}'  Role={3}  IsActive={4}" -f `
            $t.Expected, $emp.EmployeeId, $emp.FullName, $emp.PrimaryRole, $emp.IsActive) -ForegroundColor Green
        $Located += [PSCustomObject]@{
            EmployeeId   = "$($emp.EmployeeId)"
            FullName     = "$($emp.FullName)"
            PrimaryRole  = "$($emp.PrimaryRole)"
            IsActive     = "$($emp.IsActive)"
            MatchType    = "FOUND_BY_NAME"
            OrigExpected = $t.Expected
            SearchNote   = "Patterns: $($patternsUsed -join ', ')"
            Counts       = $null
        }
    } else {
        Write-Host ("  AMBIGUOUS ({0} matches): expected='{1}'  patterns={2}" -f $unique.Count, $t.Expected, ($patternsUsed -join ", ")) -ForegroundColor Yellow
        foreach ($emp in $unique) {
            Write-Host ("    ID={0}  FullName='{1}'  Role={2}  IsActive={3}" -f `
                $emp.EmployeeId, $emp.FullName, $emp.PrimaryRole, $emp.IsActive) -ForegroundColor Yellow
        }
        $Ambiguous += $t.Expected
        foreach ($emp in $unique) {
            $Located += [PSCustomObject]@{
                EmployeeId   = "$($emp.EmployeeId)"
                FullName     = "$($emp.FullName)"
                PrimaryRole  = "$($emp.PrimaryRole)"
                IsActive     = "$($emp.IsActive)"
                MatchType    = "AMBIGUOUS"
                OrigExpected = $t.Expected
                SearchNote   = "Patterns: $($patternsUsed -join ', ') -> $($unique.Count) hits -- MANUAL REVIEW"
                Counts       = $null
            }
        }
    }
}

Write-Host ""
if ($NotFound.Count -gt 0) {
    Write-Host ("  Not in Employees at all: {0}" -f ($NotFound -join ", ")) -ForegroundColor Red
    Write-Host ""
}
if ($Ambiguous.Count -gt 0) {
    Write-Host ("  Ambiguous (manual review needed): {0}" -f ($Ambiguous -join ", ")) -ForegroundColor Yellow
    Write-Host ""
}

$AllToCount = @()
foreach ($e in $Located) {
    if ($e.MatchType -ne "AMBIGUOUS") { $AllToCount += $e }
}

if ($AllToCount.Count -eq 0) {
    Write-Host "No unambiguous employees located. Exiting." -ForegroundColor Red
    $Conn.Close()
    exit 1
}

# ---------------------------------------------------------------------------
# Count 17 table/columns for every unambiguously located employee
# ---------------------------------------------------------------------------

Write-Host ("--- Row counts for {0} employee(s) ---" -f $AllToCount.Count) -ForegroundColor Cyan
Write-Host ""

$CombinedTotals = [ordered]@{
    "Employees"                  = 0
    "ShiftEntries"               = 0
    "WicShiftEntries"            = 0
    "SickLeaves"                 = 0
    "Vacations"                  = 0
    "ALBalance"                  = 0
    "SubstitutionHistory"        = 0
    "BreakSlots"                 = 0
    "VwicRotationSlots"          = 0
    "WicAgentAssignments(name)"  = 0
    "AgentReachableCities(id)"   = 0
    "AgentReachableCities(name)" = 0
    "DailyAttendances"           = 0
    "WicPipeline Primary"        = 0
    "WicPipeline Backup"         = 0
    "TrainingSchedule AgentIds"  = 0
    "TrainingSchedule SuggestBy" = 0
}

foreach ($e in $AllToCount) {
    $e.Counts = Get-Counts -Id $e.EmployeeId -Name $e.FullName
    foreach ($key in @($CombinedTotals.Keys)) {
        if ($e.Counts.Contains($key)) {
            $CombinedTotals[$key] += $e.Counts[$key]
        }
    }
    Write-Host ("  Counted: {0}  {1}" -f $e.EmployeeId, $e.FullName) -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# Per-employee breakdown
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "--- Per-Employee Row Counts ---" -ForegroundColor Cyan
Write-Host ""

foreach ($e in $AllToCount) {
    Write-Host ("{0}  {1}  (Role={2}, IsActive={3})  [FOUND_BY_NAME]" -f `
        $e.EmployeeId, $e.FullName, $e.PrimaryRole, $e.IsActive) -ForegroundColor Green
    if ($e.SearchNote -ne "") {
        Write-Host ("  Note: {0}  original target: '{1}'" -f $e.SearchNote, $e.OrigExpected) -ForegroundColor DarkGray
    }

    $rowTotal = 0
    foreach ($key in @($e.Counts.Keys)) {
        $val   = $e.Counts[$key]
        $color = if ($val -gt 0) { "Yellow" } else { "DarkGray" }
        Write-Host ("  {0,-38} {1,5}" -f $key, $val) -ForegroundColor $color
        $rowTotal += $val
    }
    Write-Host ("  {0,-38} {1,5}" -f "TOTAL", $rowTotal) -ForegroundColor Cyan
    Write-Host ""
}

# ---------------------------------------------------------------------------
# WIC impact
# ---------------------------------------------------------------------------

Write-Host "--- WIC Impact Analysis ---" -ForegroundColor Cyan
Write-Host ""

$wicFound = $false
foreach ($e in $AllToCount) {
    $wicA = $e.Counts["WicAgentAssignments(name)"]
    $wicR = $e.Counts["AgentReachableCities(id)"] + $e.Counts["AgentReachableCities(name)"]

    if ($wicA -gt 0 -or $wicR -gt 0) {
        $wicFound = $true
        Write-Host ("{0}  {1}" -f $e.EmployeeId, $e.FullName) -ForegroundColor Yellow

        if ($wicA -gt 0) {
            $aRows = @(Invoke-Rows "SELECT LocationCode, AssignmentType, IsActive, Notes FROM WicAgentAssignments WHERE EmployeeName = @n ORDER BY LocationCode" @{n=$e.FullName})
            Write-Host ("  WicAgentAssignments ({0} row(s)):" -f $aRows.Count)
            foreach ($a in $aRows) {
                $notes = if ($null -eq $a.Notes -or "$($a.Notes)" -eq "") { "" } else { "  Notes=$($a.Notes)" }
                Write-Host ("    LocationCode={0,-28} AssignmentType={1,-12} IsActive={2}{3}" -f `
                    $a.LocationCode, $a.AssignmentType, $a.IsActive, $notes) -ForegroundColor Cyan
            }
        }

        if ($wicR -gt 0) {
            $rRows = @(Invoke-Rows "SELECT EmployeeId, EmployeeName, City, Source FROM AgentReachableCities WHERE EmployeeId = @id OR EmployeeName = @n ORDER BY City" @{id=$e.EmployeeId; n=$e.FullName})
            Write-Host ("  AgentReachableCities ({0} row(s)):" -f $rRows.Count)
            foreach ($rc in $rRows) {
                Write-Host ("    City={0,-32} Source={1}" -f $rc.City, $rc.Source) -ForegroundColor Cyan
            }
        }

        Write-Host ""
    }
}

if (-not $wicFound) {
    Write-Host "  No WIC assignments or reachable-city data for any located employee." -ForegroundColor DarkGray
    Write-Host ""
}

# ---------------------------------------------------------------------------
# Combined totals
# ---------------------------------------------------------------------------

Write-Host ("--- Combined Totals ({0} employees) ---" -f $AllToCount.Count) -ForegroundColor Cyan
Write-Host ""
Write-Host ("  {0,-38} {1,6}" -f "Table/Column", "Total")
Write-Host ("  {0,-38} {1,6}" -f ("-"*38), ("-"*6))

$grandTotal = 0
foreach ($key in @($CombinedTotals.Keys)) {
    $val   = $CombinedTotals[$key]
    $color = if ($val -gt 0) { "Yellow" } else { "DarkGray" }
    Write-Host ("  {0,-38} {1,6}" -f $key, $val) -ForegroundColor $color
    $grandTotal += $val
}

Write-Host ("  {0,-38} {1,6}" -f ("-"*38), ("-"*6))
Write-Host ("  {0,-72} {1,6}" -f "GRAND TOTAL", $grandTotal) -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Final delete list summary
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=== FINAL CONSOLIDATED DELETE LIST ===" -ForegroundColor Yellow
Write-Host "(Real EmployeeId, verified FullName, total rows. Review before confirming.)"
Write-Host ""
Write-Host ("  {0,-14} {1,-36} {2,-20} {3,6}  {4}" -f "EmployeeId", "FullName", "Role", "Rows", "Status")
Write-Host ("  {0,-14} {1,-36} {2,-20} {3,6}  {4}" -f ("-"*14), ("-"*36), ("-"*20), ("-"*6), ("-"*18))

$listTotal = 0
foreach ($e in $AllToCount) {
    $rowTotal = 0
    foreach ($key in @($e.Counts.Keys)) { $rowTotal += $e.Counts[$key] }
    $listTotal += $rowTotal
    Write-Host ("  {0,-14} {1,-36} {2,-20} {3,6}  {4}" -f `
        $e.EmployeeId, $e.FullName, $e.PrimaryRole, $rowTotal, $e.MatchType) -ForegroundColor White
}

if ($NotFound.Count -gt 0) {
    Write-Host ""
    Write-Host "  Not found in Employees (excluded from delete):" -ForegroundColor Red
    foreach ($name in $NotFound) {
        Write-Host ("    NOT IN DB: {0}" -f $name) -ForegroundColor Red
    }
}
if ($Ambiguous.Count -gt 0) {
    Write-Host ""
    Write-Host "  Ambiguous matches (excluded -- confirm manually):" -ForegroundColor Yellow
    foreach ($name in $Ambiguous) {
        Write-Host ("    AMBIGUOUS: {0}" -f $name) -ForegroundColor Yellow
    }
}

Write-Host ("  {0,-14} {1,-36} {2,-20} {3,6}" -f ("-"*14), ("-"*36), ("-"*20), ("-"*6))
Write-Host ("  {0,-72} {1,6}" -f "GRAND TOTAL", $listTotal) -ForegroundColor Cyan

$Conn.Close()

Write-Host ""
Write-Host "=== PHASE 1 COMPLETE -- READ-ONLY, NOTHING DELETED ===" -ForegroundColor Yellow
Write-Host "Confirm EmployeeIds above, then proceed to Phase 2." -ForegroundColor White
Write-Host ""

Write-Host "--- Codebase References (hardcoded, from prior grep) ---" -ForegroundColor Cyan
Write-Host ""
Write-Host "  File: Backend\Services\WicCoverageService.cs  line 57-58" -ForegroundColor Yellow
Write-Host "    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)" -ForegroundColor White
Write-Host '        { "Ferenc Koreh", "Tunde Szabo", "Zsolt Fulop" };' -ForegroundColor White
Write-Host "    Used at lines 92, 141, 250, 261, 329, 340-343 (IsExcluded guard on all agent output)." -ForegroundColor DarkGray
Write-Host ""
Write-Host "  File: Backend\Services\WicCoverageImport.cs  line 10-11" -ForegroundColor Yellow
Write-Host "    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)" -ForegroundColor White
Write-Host '        { "Ferenc Koreh", "Tunde Szabo", "Zsolt Fulop" };' -ForegroundColor White
Write-Host "    Used at lines 322, 327 (skips these names during CSV import seeding)." -ForegroundColor DarkGray
Write-Host ""
Write-Host "  NOTE: These guards were added specifically because these 3 agents were being" -ForegroundColor DarkGray
Write-Host "  excluded as 2nd-level agents in WIC coverage/substitution logic. After DB" -ForegroundColor DarkGray
Write-Host "  delete, the Excluded HashSet entries can be removed from both files -- they" -ForegroundColor DarkGray
Write-Host "  will no longer be importable anyway, but keeping them is also harmless." -ForegroundColor DarkGray
Write-Host ""
