# batch_phase1_find.ps1  -- REVISED
# PHASE 1: Read-only investigation for 9 target employees.
#   A) Lookup each by EmployeeId.
#   B) For IDs not found (or name-mismatch), search Employees by last name
#      using LIKE with Latin1_General_CI_AI (case+accent insensitive).
#   C) Count 17 table/columns for every matched employee.
#   D) WIC assignment and reachable-city detail for impacted employees.
#   E) Final consolidated delete list. Nothing is deleted.
#
# Run with: pwsh -File C:\GSDDashboard\batch_phase1_find.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Add-Type -AssemblyName "System.Data"

$Targets = @(
    [PSCustomObject]@{ Id="9125518"; Expected="Maik Kopperschmidt"    },
    [PSCustomObject]@{ Id="9120969"; Expected="Thugipan Sivanesan"     },
    [PSCustomObject]@{ Id="9126885"; Expected="Uwe Sprejz"             },
    [PSCustomObject]@{ Id="9126816"; Expected="Walfredo Wester"        },
    [PSCustomObject]@{ Id="9126878"; Expected="Ebubekir Yildiz"        },
    [PSCustomObject]@{ Id="9129430"; Expected="Salih Medik"            },
    [PSCustomObject]@{ Id="9130652"; Expected="Christos Kyrillidis"    },
    [PSCustomObject]@{ Id="9122677"; Expected="Dennis Obazee"          },
    [PSCustomObject]@{ Id="9124147"; Expected="Mohamed Khaled Mahmoud" }
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
    $c["Employees"]                  = Count-Sql "SELECT COUNT(*) FROM Employees            WHERE EmployeeId         = @id"                             @{id=$Id}
    $c["ShiftEntries"]               = Count-Sql "SELECT COUNT(*) FROM ShiftEntries         WHERE EmployeeId         = @id"                             @{id=$Id}
    $c["WicShiftEntries"]            = Count-Sql "SELECT COUNT(*) FROM WicShiftEntries      WHERE EmployeeId         = @id"                             @{id=$Id}
    $c["SickLeaves"]                 = Count-Sql "SELECT COUNT(*) FROM SickLeaves           WHERE EmployeeId         = @id"                             @{id=$Id}
    $c["Vacations"]                  = Count-Sql "SELECT COUNT(*) FROM Vacations            WHERE EmployeeId         = @id"                             @{id=$Id}
    $c["ALBalance"]                  = Count-Sql "SELECT COUNT(*) FROM ALBalance            WHERE EmployeeId         = @id"                             @{id=$Id}
    $c["SubstitutionHistory"]        = Count-Sql "SELECT COUNT(*) FROM SubstitutionHistory  WHERE EmployeeId         = @id"                             @{id=$Id}
    $c["BreakSlots"]                 = Count-Sql "SELECT COUNT(*) FROM BreakSlots           WHERE EmployeeId         = @id"                             @{id=$Id}
    $c["VwicRotationSlots"]          = Count-Sql "SELECT COUNT(*) FROM VwicRotationSlots    WHERE EmployeeId         = @id"                             @{id=$Id}
    $c["WicAgentAssignments(name)"]  = Count-Sql "SELECT COUNT(*) FROM WicAgentAssignments  WHERE EmployeeName       = @n"                              @{n=$Name}
    $c["AgentReachableCities(id)"]   = Count-Sql "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeId        = @id"                             @{id=$Id}
    $c["AgentReachableCities(name)"] = Count-Sql "SELECT COUNT(*) FROM AgentReachableCities WHERE EmployeeName      = @n"                              @{n=$Name}
    $c["DailyAttendances"]           = Count-Sql "SELECT COUNT(*) FROM DailyAttendance      WHERE AssignedEmployeeId = @id"                             @{id=$Id}
    $c["WicPipeline Primary"]        = Count-Sql "SELECT COUNT(*) FROM WicPipeline          WHERE PrimaryAgent       = @n"                              @{n=$Name}
    $c["WicPipeline Backup"]         = Count-Sql "SELECT COUNT(*) FROM WicPipeline          WHERE BackupAgent        = @n"                              @{n=$Name}
    $c["TrainingSchedule AgentIds"]  = Count-Sql "SELECT COUNT(*) FROM TrainingSchedule     WHERE AgentIds LIKE @pat"                                   @{pat=$pat}
    $c["TrainingSchedule SuggestBy"] = Count-Sql "SELECT COUNT(*) FROM TrainingSchedule     WHERE SuggestedBy = @n OR ConfirmedBy = @n"                @{n=$Name}
    return $c
}

# ---------------------------------------------------------------------------
# Phase A: lookup by EmployeeId
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=== PHASE 1 (REVISED): BATCH READ-ONLY INVESTIGATION ===" -ForegroundColor Yellow
Write-Host "Nothing will be deleted."                                   -ForegroundColor Yellow
Write-Host ""
Write-Host "--- Phase A: Lookup by EmployeeId ---" -ForegroundColor Cyan
Write-Host ""

$IdVerified   = @()
$NotFoundById = @()

foreach ($t in $Targets) {
    $rows = @(Invoke-Rows "SELECT EmployeeId, FullName, PrimaryRole, IsActive FROM Employees WHERE EmployeeId = @id" @{id=$t.Id})

    if ($rows.Count -eq 0) {
        Write-Host ("  NOT FOUND by ID: {0}  expected='{1}'" -f $t.Id, $t.Expected) -ForegroundColor DarkYellow
        $NotFoundById += $t
        continue
    }

    $emp        = $rows[0]
    $actualName = if ($null -eq $emp.FullName -or "$($emp.FullName)" -eq "") { "" } else { "$($emp.FullName)" }

    if ($actualName -ne $t.Expected) {
        Write-Host ("  ID {0} found but name mismatch: DB='{1}' expected='{2}' -> will search by name" -f $t.Id, $actualName, $t.Expected) -ForegroundColor DarkYellow
        $NotFoundById += $t
        continue
    }

    Write-Host ("  OK (by ID):      {0}  '{1}'  Role={2}  IsActive={3}" -f $t.Id, $actualName, $emp.PrimaryRole, $emp.IsActive) -ForegroundColor Green
    $IdVerified += [PSCustomObject]@{
        EmployeeId   = $t.Id
        FullName     = $actualName
        PrimaryRole  = "$($emp.PrimaryRole)"
        IsActive     = "$($emp.IsActive)"
        MatchType    = "VERIFIED_BY_ID"
        OrigExpected = $t.Expected
        SearchNote   = ""
        Counts       = $null
    }
}

Write-Host ""
Write-Host ("  ID-verified: {0}   Will search by name: {1}" -f $IdVerified.Count, $NotFoundById.Count) -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Phase B: name search for not-found targets
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host ("--- Phase B: Name search (last-name LIKE, CI_AI) for {0} not-found target(s) ---" -f $NotFoundById.Count) -ForegroundColor Cyan
Write-Host ""

$FoundByName = @()
$TrulyAbsent = @()

foreach ($t in $NotFoundById) {
    $lastName  = $t.Expected.Trim().Split(" ")[-1]
    $searchPat = "%$lastName%"

    $rows = @(Invoke-Rows "SELECT EmployeeId, FullName, PrimaryRole, IsActive FROM Employees WHERE FullName LIKE @pat COLLATE Latin1_General_CI_AI ORDER BY FullName" @{pat=$searchPat})

    if ($rows.Count -eq 0) {
        Write-Host ("  TRULY ABSENT:    expected='{0}'  searched='%{1}%'  -> 0 results in Employees" -f $t.Expected, $lastName) -ForegroundColor Red
        $TrulyAbsent += $t.Expected
        continue
    }

    if ($rows.Count -eq 1) {
        $emp        = $rows[0]
        $actualId   = "$($emp.EmployeeId)"
        $actualName = "$($emp.FullName)"
        Write-Host ("  FOUND (1 match): expected='{0}'  -> ID={1}  DB='{2}'  Role={3}  IsActive={4}" -f $t.Expected, $actualId, $actualName, $emp.PrimaryRole, $emp.IsActive) -ForegroundColor Green
        $FoundByName += [PSCustomObject]@{
            EmployeeId   = $actualId
            FullName     = $actualName
            PrimaryRole  = "$($emp.PrimaryRole)"
            IsActive     = "$($emp.IsActive)"
            MatchType    = "FOUND_BY_NAME"
            OrigExpected = $t.Expected
            SearchNote   = "Searched '%$lastName%'"
            Counts       = $null
        }
    } else {
        Write-Host ("  AMBIGUOUS ({0} matches): expected='{1}'  searched='%{2}%'" -f $rows.Count, $t.Expected, $lastName) -ForegroundColor Yellow
        foreach ($emp in $rows) {
            Write-Host ("    ID={0}  FullName='{1}'  Role={2}  IsActive={3}" -f $emp.EmployeeId, $emp.FullName, $emp.PrimaryRole, $emp.IsActive) -ForegroundColor Yellow
        }
        foreach ($emp in $rows) {
            $actualId   = "$($emp.EmployeeId)"
            $actualName = "$($emp.FullName)"
            $FoundByName += [PSCustomObject]@{
                EmployeeId   = $actualId
                FullName     = $actualName
                PrimaryRole  = "$($emp.PrimaryRole)"
                IsActive     = "$($emp.IsActive)"
                MatchType    = "AMBIGUOUS"
                OrigExpected = $t.Expected
                SearchNote   = "Searched '%$lastName%' -> $($rows.Count) hits - MANUAL REVIEW"
                Counts       = $null
            }
        }
    }
}

Write-Host ""
if ($TrulyAbsent.Count -gt 0) {
    Write-Host ("  Truly absent (not in Employees at all): {0}" -f ($TrulyAbsent -join ", ")) -ForegroundColor Red
    Write-Host ""
}

# Build master list: ID-verified first, then found-by-name
$AllToCount = @()
foreach ($e in $IdVerified)  { $AllToCount += $e }
foreach ($e in $FoundByName) { $AllToCount += $e }

if ($AllToCount.Count -eq 0) {
    Write-Host "No employees located. Exiting." -ForegroundColor Red
    $Conn.Close()
    exit 1
}

# ---------------------------------------------------------------------------
# Phase C: count 17 table/columns for every located employee
# ---------------------------------------------------------------------------

Write-Host ("--- Phase C: Row counts for {0} located employee(s) ---" -f $AllToCount.Count) -ForegroundColor Cyan
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
    # Iterate over a COPY of keys -- avoids "Collection was modified" on OrderedDictionary
    foreach ($key in @($CombinedTotals.Keys)) {
        if ($e.Counts.Contains($key)) {
            $CombinedTotals[$key] += $e.Counts[$key]
        }
    }
    Write-Host ("  Counted: {0}  {1}" -f $e.EmployeeId, $e.FullName) -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# Print per-employee breakdown
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "--- Per-Employee Row Counts ---" -ForegroundColor Cyan
Write-Host ""

foreach ($e in $AllToCount) {
    $tag = if ($e.MatchType -eq "VERIFIED_BY_ID") {
        "[ID-verified]"
    } elseif ($e.MatchType -eq "FOUND_BY_NAME") {
        "[found-by-name]"
    } else {
        "[AMBIGUOUS - review required]"
    }
    $tagColor = if ($e.MatchType -eq "AMBIGUOUS") { "Yellow" } else { "Green" }

    Write-Host ("{0}  {1}  (Role={2}, IsActive={3})  {4}" -f $e.EmployeeId, $e.FullName, $e.PrimaryRole, $e.IsActive, $tag) -ForegroundColor $tagColor
    if ($null -ne $e.SearchNote -and $e.SearchNote -ne "") {
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
# WIC impact: detail rows for anyone with assignments or reachable cities
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
                Write-Host ("    LocationCode={0,-28} AssignmentType={1,-12} IsActive={2}{3}" -f $a.LocationCode, $a.AssignmentType, $a.IsActive, $notes) -ForegroundColor Cyan
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
# Iterate over COPY of keys to avoid "Collection was modified" error
foreach ($key in @($CombinedTotals.Keys)) {
    $val   = $CombinedTotals[$key]
    $color = if ($val -gt 0) { "Yellow" } else { "DarkGray" }
    Write-Host ("  {0,-38} {1,6}" -f $key, $val) -ForegroundColor $color
    $grandTotal += $val
}

Write-Host ("  {0,-38} {1,6}" -f ("-"*38), ("-"*6))
Write-Host ("  {0,-38} {1,6}" -f "GRAND TOTAL", $grandTotal) -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Final consolidated delete list
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=== FINAL CONSOLIDATED DELETE LIST ===" -ForegroundColor Yellow
Write-Host "(Real EmployeeId, verified FullName, total rows. AMBIGUOUS = needs manual confirmation.)"
Write-Host ""
Write-Host ("  {0,-14} {1,-36} {2,-20} {3,6}  {4}" -f "EmployeeId", "FullName", "Role", "Rows", "Status")
Write-Host ("  {0,-14} {1,-36} {2,-20} {3,6}  {4}" -f ("-"*14), ("-"*36), ("-"*20), ("-"*6), ("-"*22))

$listTotal = 0
foreach ($e in $AllToCount) {
    $rowTotal = 0
    foreach ($key in @($e.Counts.Keys)) { $rowTotal += $e.Counts[$key] }
    $listTotal += $rowTotal
    $color = if ($e.MatchType -eq "AMBIGUOUS") { "Yellow" } else { "White" }
    Write-Host ("  {0,-14} {1,-36} {2,-20} {3,6}  {4}" -f $e.EmployeeId, $e.FullName, $e.PrimaryRole, $rowTotal, $e.MatchType) -ForegroundColor $color
}

if ($TrulyAbsent.Count -gt 0) {
    Write-Host ""
    Write-Host "  Not found in Employees (excluded):" -ForegroundColor Red
    foreach ($name in $TrulyAbsent) {
        Write-Host ("    NOT IN DB: {0}" -f $name) -ForegroundColor Red
    }
}

Write-Host ("  {0,-14} {1,-36} {2,-20} {3,6}" -f ("-"*14), ("-"*36), ("-"*20), ("-"*6))
Write-Host ("  {0,-72} {1,6}" -f "GRAND TOTAL", $listTotal) -ForegroundColor Cyan

$Conn.Close()

Write-Host ""
Write-Host "=== PHASE 1 COMPLETE -- READ-ONLY, NOTHING DELETED ===" -ForegroundColor Yellow
Write-Host "Resolve AMBIGUOUS entries if any, then confirm to proceed to Phase 2." -ForegroundColor White
Write-Host ""
