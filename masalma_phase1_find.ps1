# masalma_phase1_find.ps1
# PHASE 1: Read-only investigation for Mohammad Al Masalama employee update.
#   1. Find his Employees row.
#   2. Find every name-keyed reference to his current FullName.
#   3. Report the update plan. Nothing is changed.
#
# Run with: pwsh -File C:\GSDDashboard\masalma_phase1_find.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Add-Type -AssemblyName "System.Data"

$OldName = "Mohammad Al Masalama"

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

Write-Host ""
Write-Host "=== MASALMA PHASE 1: READ-ONLY INVESTIGATION ===" -ForegroundColor Yellow
Write-Host "Nothing will be changed."                          -ForegroundColor Yellow
Write-Host ""

# ---------------------------------------------------------------------------
# Step 1: Find the Employees row
# ---------------------------------------------------------------------------

Write-Host "--- Step 1: Employees row (LIKE '%Al Masal%') ---" -ForegroundColor Cyan
Write-Host ""

$empRows = @(Invoke-Rows "SELECT EmployeeId, FullName, PrimaryKid, InfosysEmail, EonEmail, PrimaryRole, IsActive FROM Employees WHERE FullName LIKE '%Al Masal%' COLLATE Latin1_General_CI_AI ORDER BY FullName")

if ($empRows.Count -eq 0) {
    Write-Host "  NOT FOUND: no row matching '%Al Masal%' in Employees." -ForegroundColor Red
    $Conn.Close()
    exit 1
}

if ($empRows.Count -gt 1) {
    Write-Host ("  AMBIGUOUS: {0} rows found -- manual review required." -f $empRows.Count) -ForegroundColor Red
    foreach ($r in $empRows) {
        Write-Host ("    ID={0}  FullName='{1}'" -f $r.EmployeeId, $r.FullName) -ForegroundColor Yellow
    }
    $Conn.Close()
    exit 1
}

$emp = $empRows[0]
$EmpId      = "$($emp.EmployeeId)"
$CurrentName = "$($emp.FullName)"

$fmt = "  {0,-18} {1}"
Write-Host ($fmt -f "EmployeeId:",   $EmpId)                                     -ForegroundColor White
Write-Host ($fmt -f "FullName:",     $CurrentName)                               -ForegroundColor White
Write-Host ($fmt -f "PrimaryKid:",   $(if ($null -eq $emp.PrimaryKid)   { "(null)" } else { $emp.PrimaryKid }))   -ForegroundColor White
Write-Host ($fmt -f "InfosysEmail:", $(if ($null -eq $emp.InfosysEmail) { "(null)" } else { $emp.InfosysEmail })) -ForegroundColor White
Write-Host ($fmt -f "EonEmail:",     $(if ($null -eq $emp.EonEmail)     { "(null)" } else { $emp.EonEmail }))     -ForegroundColor White
Write-Host ($fmt -f "PrimaryRole:",  $emp.PrimaryRole)                          -ForegroundColor White
Write-Host ($fmt -f "IsActive:",     $emp.IsActive)                             -ForegroundColor White
Write-Host ""

# ---------------------------------------------------------------------------
# Step 2: Name-keyed references to current FullName
# ---------------------------------------------------------------------------

Write-Host ("--- Step 2: Name-keyed references to '{0}' ---" -f $CurrentName) -ForegroundColor Cyan
Write-Host ""

# WicAgentAssignments
$waaRows = @(Invoke-Rows "SELECT Id, LocationCode, AssignmentType, IsActive, Notes FROM WicAgentAssignments WHERE EmployeeName = @n ORDER BY LocationCode, AssignmentType" @{n=$CurrentName})
Write-Host ("  WicAgentAssignments ({0} row(s)):" -f $waaRows.Count) -ForegroundColor $(if ($waaRows.Count -gt 0) { "Yellow" } else { "DarkGray" })
foreach ($r in $waaRows) {
    $notes = if ($null -eq $r.Notes -or "$($r.Notes)" -eq "") { "" } else { "  Notes=$($r.Notes)" }
    Write-Host ("    Id={0,-6} LocationCode={1,-28} AssignmentType={2,-8} IsActive={3}{4}" -f $r.Id, $r.LocationCode, $r.AssignmentType, $r.IsActive, $notes) -ForegroundColor Cyan
}
Write-Host ""

# AgentReachableCities
$arcRows = @(Invoke-Rows "SELECT Id, EmployeeId, City, Source FROM AgentReachableCities WHERE EmployeeName = @n ORDER BY City" @{n=$CurrentName})
Write-Host ("  AgentReachableCities ({0} row(s)):" -f $arcRows.Count) -ForegroundColor $(if ($arcRows.Count -gt 0) { "Yellow" } else { "DarkGray" })
foreach ($r in $arcRows) {
    Write-Host ("    Id={0,-6} EmployeeId={1,-10} City={2,-32} Source={3}" -f $r.Id, $r.EmployeeId, $r.City, $r.Source) -ForegroundColor Cyan
}
Write-Host ""

# WicPipeline - PrimaryAgent
$wpPrimRows = @(Invoke-Rows "SELECT Id, LocationCode, WeekStart, PrimaryAgent FROM WicPipeline WHERE PrimaryAgent = @n ORDER BY LocationCode, WeekStart" @{n=$CurrentName})
Write-Host ("  WicPipeline.PrimaryAgent ({0} row(s)):" -f $wpPrimRows.Count) -ForegroundColor $(if ($wpPrimRows.Count -gt 0) { "Yellow" } else { "DarkGray" })
foreach ($r in $wpPrimRows) {
    Write-Host ("    Id={0,-6} LocationCode={1,-28} WeekStart={2}" -f $r.Id, $r.LocationCode, $r.WeekStart) -ForegroundColor Cyan
}
Write-Host ""

# WicPipeline - BackupAgent
$wpBackRows = @(Invoke-Rows "SELECT Id, LocationCode, WeekStart, BackupAgent FROM WicPipeline WHERE BackupAgent = @n ORDER BY LocationCode, WeekStart" @{n=$CurrentName})
Write-Host ("  WicPipeline.BackupAgent ({0} row(s)):" -f $wpBackRows.Count) -ForegroundColor $(if ($wpBackRows.Count -gt 0) { "Yellow" } else { "DarkGray" })
foreach ($r in $wpBackRows) {
    Write-Host ("    Id={0,-6} LocationCode={1,-28} WeekStart={2}" -f $r.Id, $r.LocationCode, $r.WeekStart) -ForegroundColor Cyan
}
Write-Host ""

# TrainingSchedule - SuggestedBy / ConfirmedBy
$tsRows = @(Invoke-Rows "SELECT Id, SuggestedBy, ConfirmedBy FROM TrainingSchedule WHERE SuggestedBy = @n OR ConfirmedBy = @n ORDER BY Id" @{n=$CurrentName})
Write-Host ("  TrainingSchedule SuggestedBy/ConfirmedBy ({0} row(s)):" -f $tsRows.Count) -ForegroundColor $(if ($tsRows.Count -gt 0) { "Yellow" } else { "DarkGray" })
foreach ($r in $tsRows) {
    Write-Host ("    Id={0,-6} SuggestedBy={1,-32} ConfirmedBy={2}" -f $r.Id, $r.SuggestedBy, $r.ConfirmedBy) -ForegroundColor Cyan
}
Write-Host ""

$Conn.Close()

# ---------------------------------------------------------------------------
# Step 3: Update plan
# ---------------------------------------------------------------------------

Write-Host "--- Step 3: Update plan ---" -ForegroundColor Cyan
Write-Host ""
Write-Host "  DB: Employees row (EmployeeId = $EmpId)" -ForegroundColor White
Write-Host "    FullName:   '$CurrentName'  ->  'Mohammad Al Masalma'"        -ForegroundColor Yellow
Write-Host "    PrimaryKid: (current above)  ->  'M101365'"                   -ForegroundColor Yellow
Write-Host "    EonEmail:   (current above)  ->  'Mohammad.Al.Masalma.external@eon.com'"  -ForegroundColor Yellow
Write-Host "    (PrimaryRole, IsActive, InfosysEmail: unchanged)"             -ForegroundColor DarkGray
Write-Host ""
Write-Host "  DB: Name-keyed rows to rename '$CurrentName' -> 'Mohammad Al Masalma':" -ForegroundColor White
Write-Host ("    WicAgentAssignments.EmployeeName    {0,4} row(s)" -f $waaRows.Count)    -ForegroundColor $(if ($waaRows.Count  -gt 0) { "Yellow" } else { "DarkGray" })
Write-Host ("    AgentReachableCities.EmployeeName   {0,4} row(s)" -f $arcRows.Count)    -ForegroundColor $(if ($arcRows.Count  -gt 0) { "Yellow" } else { "DarkGray" })
Write-Host ("    WicPipeline.PrimaryAgent            {0,4} row(s)" -f $wpPrimRows.Count) -ForegroundColor $(if ($wpPrimRows.Count -gt 0) { "Yellow" } else { "DarkGray" })
Write-Host ("    WicPipeline.BackupAgent             {0,4} row(s)" -f $wpBackRows.Count) -ForegroundColor $(if ($wpBackRows.Count -gt 0) { "Yellow" } else { "DarkGray" })
Write-Host ("    TrainingSchedule SuggestBy/Confirm  {0,4} row(s)" -f $tsRows.Count)    -ForegroundColor $(if ($tsRows.Count    -gt 0) { "Yellow" } else { "DarkGray" })
Write-Host ""
Write-Host "  CODE (must be updated in same step, or import re-seeds the old name):" -ForegroundColor White
Write-Host "    Backend\Services\WicCoverageImport.cs line 140:" -ForegroundColor Yellow
Write-Host "      AgentSeeds record: 'Mohammad Al Masalama' -> 'Mohammad Al Masalma'" -ForegroundColor Yellow
Write-Host "      Also: fill in PrimaryKid='M101365', EonEmail='Mohammad.Al.Masalma.external@eon.com'" -ForegroundColor Yellow
Write-Host "    Backend\Services\WicCoverageImport.cs line 204:" -ForegroundColor Yellow
Write-Host "      WicSeeds Neu-Isenburg primary list: 'Mohammad Al Masalama' -> 'Mohammad Al Masalma'" -ForegroundColor Yellow
Write-Host ""

Write-Host "=== PHASE 1 COMPLETE -- READ-ONLY, NOTHING CHANGED ===" -ForegroundColor Yellow
Write-Host "Confirm EmployeeId + counts above, then proceed to Phase 2." -ForegroundColor White
Write-Host ""
