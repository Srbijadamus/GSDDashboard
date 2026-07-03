# teresa_phase1_find.ps1
# PHASE 1: Read-only investigation of Teresa Kwasniewska across all tables.
# No rows are deleted or modified. Review output and confirm before Phase 2.
#
# Run with: pwsh -File C:\GSDDashboard\teresa_phase1_find.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Add-Type -AssemblyName "System.Data"

function Open-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection($script:ConnString)
    $c.Open()
    return $c
}

function Invoke-SqlRows([string]$Sql, [hashtable]$Params = @{}) {
    $conn = Open-Conn
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Sql
        foreach ($kv in $Params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        $rows = @()
        $reader = $cmd.ExecuteReader()
        while ($reader.Read()) {
            $row = @{}
            for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                $v = $reader.GetValue($i)
                $row[$reader.GetName($i)] = if ($v -is [System.DBNull]) { $null } else { $v }
            }
            $rows += [PSCustomObject]$row
        }
        $reader.Close()
        return $rows
    } finally { $conn.Close() }
}

function Invoke-SqlScalar([string]$Sql, [hashtable]$Params = @{}) {
    $conn = Open-Conn
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Sql
        foreach ($kv in $Params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        $v = $cmd.ExecuteScalar()
        $result = if ($v -is [System.DBNull]) { $null } else { $v }
        return $result
    } finally { $conn.Close() }
}

Write-Host ""
Write-Host "=== PHASE 1: Find Teresa Kwasniewska (READ-ONLY) ===" -ForegroundColor Yellow
Write-Host ""

# ---------------------------------------------------------------------------
# Step 1: Locate the Employees row -- must be exactly one match
# ---------------------------------------------------------------------------
Write-Host "Step 1: SELECT from Employees WHERE FullName LIKE '%Kwasniewska%' ..." -ForegroundColor Cyan

$empRows = Invoke-SqlRows "SELECT EmployeeId, FullName, PrimaryRole, SecondaryRole, IsActive, IsTrainee, Category, TeamLeadName FROM Employees WHERE FullName LIKE '%Kwasniewska%'"

if ($empRows.Count -eq 0) {
    Write-Host "  STOP: No match found. Cannot proceed." -ForegroundColor Red
    exit 1
}
if ($empRows.Count -gt 1) {
    Write-Host "  STOP: Multiple matches found -- cannot proceed without a unique match:" -ForegroundColor Red
    foreach ($r in $empRows) {
        Write-Host ("    EmployeeId={0}  FullName={1}  IsActive={2}" -f $r.EmployeeId, $r.FullName, $r.IsActive)
    }
    exit 1
}

$emp = $empRows[0]
$EmpId   = "$($emp.EmployeeId)"
$EmpName = "$($emp.FullName)"

Write-Host ("  FOUND: EmployeeId={0}  FullName={1}" -f $EmpId, $EmpName) -ForegroundColor Green
Write-Host ("         PrimaryRole={0}  SecondaryRole={1}  IsActive={2}  IsTrainee={3}" -f `
    $emp.PrimaryRole, $emp.SecondaryRole, $emp.IsActive, $emp.IsTrainee)
Write-Host ("         Category={0}  TeamLeadName={1}" -f $emp.Category, $emp.TeamLeadName)
Write-Host ""

# ---------------------------------------------------------------------------
# Step 2: Confirm what makes her eligible as a substitute candidate
# ---------------------------------------------------------------------------
Write-Host "Step 2: Substitute eligibility check ..." -ForegroundColor Cyan

$wicAssignRows = Invoke-SqlRows "SELECT Id, LocationCode, AssignmentType, IsActive, Notes FROM WicAgentAssignments WHERE EmployeeName = @n" -Params @{n=$EmpName}
$reachRows     = Invoke-SqlRows "SELECT Id, EmployeeId, EmployeeName, City, Source FROM AgentReachableCities WHERE EmployeeId = @id OR EmployeeName = @n" -Params @{id=$EmpId; n=$EmpName}

Write-Host ("  Employees.IsActive = {0}  PrimaryRole = {1}" -f $emp.IsActive, $emp.PrimaryRole)
Write-Host ("  WicAgentAssignments rows (by EmployeeName): {0}" -f $wicAssignRows.Count)
if ($wicAssignRows.Count -gt 0) {
    foreach ($r in $wicAssignRows) {
        Write-Host ("    Id={0}  LocationCode={1}  AssignmentType={2}  IsActive={3}" -f $r.Id, $r.LocationCode, $r.AssignmentType, $r.IsActive)
    }
}
Write-Host ("  AgentReachableCities rows (by EmployeeId or EmployeeName): {0}" -f $reachRows.Count)
if ($reachRows.Count -gt 0) {
    foreach ($r in $reachRows) {
        Write-Host ("    Id={0}  EmployeeId={1}  EmployeeName={2}  City={3}  Source={4}" -f $r.Id, $r.EmployeeId, $r.EmployeeName, $r.City, $r.Source)
    }
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 3: Row counts in every employee-referencing table
# ---------------------------------------------------------------------------
Write-Host "Step 3: Row counts across all employee-referencing tables ..." -ForegroundColor Cyan
Write-Host ""

function Count-Rows([string]$Table, [string]$Condition, [hashtable]$Params = @{}) {
    $sql = "SELECT COUNT(*) FROM $Table WHERE $Condition"
    $v = Invoke-SqlScalar -Sql $sql -Params $Params
    $n = if ($null -eq $v) { 0 } else { [int]$v }
    return $n
}

$counts = [ordered]@{}

$counts["Employees"]           = Count-Rows "Employees"           "EmployeeId = @id"                   @{id=$EmpId}
$counts["ShiftEntries"]        = Count-Rows "ShiftEntries"        "EmployeeId = @id"                   @{id=$EmpId}
$counts["WicShiftEntries"]     = Count-Rows "WicShiftEntries"     "EmployeeId = @id"                   @{id=$EmpId}
$counts["SickLeaves"]          = Count-Rows "SickLeaves"          "EmployeeId = @id"                   @{id=$EmpId}
$counts["Vacations"]           = Count-Rows "Vacations"           "EmployeeId = @id"                   @{id=$EmpId}
$counts["ALBalance"]           = Count-Rows "ALBalance"           "EmployeeId = @id"                   @{id=$EmpId}
$counts["SubstitutionHistory"] = Count-Rows "SubstitutionHistory" "EmployeeId = @id"                   @{id=$EmpId}
$counts["BreakSlots"]          = Count-Rows "BreakSlots"          "EmployeeId = @id"                   @{id=$EmpId}
$counts["VwicRotationSlots"]   = Count-Rows "VwicRotationSlots"   "EmployeeId = @id"                   @{id=$EmpId}

$counts["WicAgentAssignments"] = Count-Rows "WicAgentAssignments" "EmployeeName = @n"                  @{n=$EmpName}
$counts["AgentReachableCities (by ID)"]   = Count-Rows "AgentReachableCities" "EmployeeId = @id"      @{id=$EmpId}
$counts["AgentReachableCities (by Name)"] = Count-Rows "AgentReachableCities" "EmployeeName = @n"     @{n=$EmpName}

$counts["DailyAttendances"]    = Count-Rows "DailyAttendance"     "AssignedEmployeeId = @id"           @{id=$EmpId}

$counts["WicPipeline(Primary)"] = Count-Rows "WicPipeline"        "PrimaryAgent = @n"                 @{n=$EmpName}
$counts["WicPipeline(Backup)"]  = Count-Rows "WicPipeline"        "BackupAgent  = @n"                 @{n=$EmpName}

$counts["TrainingSchedule(AgentIds)"]  = Count-Rows "TrainingSchedule" "AgentIds LIKE @pat"            @{pat="%$EmpId%"}
$counts["TrainingSchedule(SuggestBy)"] = Count-Rows "TrainingSchedule" "SuggestedBy = @n OR ConfirmedBy = @n" @{n=$EmpName}

Write-Host ("  {0,-40} {1}" -f "Table / Column", "Rows")
Write-Host ("  {0,-40} {1}" -f ("-"*40), ("-"*6))
foreach ($kv in $counts.GetEnumerator()) {
    $color = if ($kv.Value -gt 0) { "Yellow" } else { "DarkGray" }
    Write-Host ("  {0,-40} {1}" -f $kv.Key, $kv.Value) -ForegroundColor $color
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 4: Summary -- STOP here, no deletes performed
# ---------------------------------------------------------------------------
$nonZero = ($counts.Values | Where-Object { $_ -gt 0 }).Count

Write-Host "=== PHASE 1 COMPLETE -- READ-ONLY, NOTHING DELETED ===" -ForegroundColor Yellow
Write-Host ""
Write-Host ("  EmployeeId : {0}" -f $EmpId)   -ForegroundColor Cyan
Write-Host ("  FullName   : {0}" -f $EmpName) -ForegroundColor Cyan
Write-Host ("  Tables with data: {0} of {1}" -f $nonZero, $counts.Count) -ForegroundColor Cyan
Write-Host ""
Write-Host "Review the counts above, then confirm to proceed to Phase 2 (delete)." -ForegroundColor White
Write-Host ""
