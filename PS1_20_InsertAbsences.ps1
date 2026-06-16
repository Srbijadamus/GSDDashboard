# PS1_20_InsertAbsences.ps1
# Inserts planned absences into SickLeaves for 2026-06-16.
#
# BEFORE RUNNING -- read these two notes:
#
# NOTE 1 -- LeaveType placeholders:
#   Dennis Markus and Ayten Karatas are confirmed SL (starting 2026-06-15).
#   The remaining 18 entries are marked LeaveType="AL" as a placeholder.
#   Edit the $entries array below to set the correct type for each person
#   before running. Valid values: AL, SL, OFF, CD, HALF_AL, UL.
#
# NOTE 2 -- Table scope:
#   SickLeaves feeds the Sick Leave report page only.
#   WIC coverage / forecast / briefing reads ShiftEntries.ShiftType,
#   NOT SickLeaves. These inserts will NOT affect the Overview coverage display.
#   If absences must affect WIC coverage, a separate ShiftEntries insert is needed.

$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"

# ── Entry table ───────────────────────────────────────────────────────────────
# Columns: Name (must match Employees.FullName exactly), LeaveType, FirstDay, LastDay
# Confirmed types: Dennis Markus = SL (2026-06-15), Ayten Karatas = SL (2026-06-15)
# All others: LeaveType = "AL" PLACEHOLDER -- verify before running

$entries = @(
    [PSCustomObject]@{ Name="Abdulrahman Aldera";    LeaveType="AL";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Angelika Weber";         LeaveType="AL";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Krishnendu Das";         LeaveType="AL";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Mark Bachmann";          LeaveType="SL";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Rene Altmeyer";          LeaveType="AL";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Yun Hee Oh";             LeaveType="OFF"; FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Darjusch Dropczinsky";   LeaveType="AL";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Eva-Liane Schliwa";      LeaveType="CD";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Anil Bedzeti";           LeaveType="SL";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Gunter Dinkelmann";      LeaveType="AL";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Ahmed Hasanovic";        LeaveType="SL";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Arevig Ketenjian";       LeaveType="OFF"; FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Christian Pastors";      LeaveType="OFF"; FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Mustafa Deveci";         LeaveType="OFF"; FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Zehra Sila Görgün";      LeaveType="OFF"; FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Veronika Kouwui";        LeaveType="OFF"; FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Kemal Sener";            LeaveType="OFF"; FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Pascal Dutz";            LeaveType="SL";  FirstDay="2026-06-16"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Dennis Markus";          LeaveType="SL";  FirstDay="2026-06-15"; LastDay="2026-06-16" },
    [PSCustomObject]@{ Name="Ayten Karatas";          LeaveType="SL";  FirstDay="2026-06-15"; LastDay="2026-06-16" }
)

# ── Connect ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== PS1_20: Insert Absences ===" -ForegroundColor Yellow
Write-Host ""

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
try {
    $conn.Open()
    Write-Host "DB connection: OK" -ForegroundColor Green
} catch {
    Write-Host "ERROR: Cannot connect to DB -- $_" -ForegroundColor Red
    exit 1
}

# ── Step 1: Lookup EmployeeIds ────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Step 1: Lookup EmployeeIds ---" -ForegroundColor Cyan

# Build IN list for a single round-trip
$nameList = ($entries | ForEach-Object { "'" + $_.Name.Replace("'","''") + "'" }) -join ","
$lookupSql = "SELECT EmployeeId, FullName, FirstName, LastName, TeamLeadName FROM Employees WHERE FullName IN ($nameList)"

$lookupCmd = New-Object System.Data.SqlClient.SqlCommand($lookupSql, $conn)
$reader    = $lookupCmd.ExecuteReader()

$empMap = @{}   # FullName -> { EmployeeId, FirstName, LastName, TeamLeadName }
while ($reader.Read()) {
    $fn = ([string]$reader["FullName"]).Trim()
    if ($fn) {
        $empMap[$fn] = @{
            EmployeeId   = [string]$reader["EmployeeId"]
            FirstName    = [string]$reader["FirstName"]
            LastName     = [string]$reader["LastName"]
            TeamLeadName = [string]$reader["TeamLeadName"]
        }
    }
}
$reader.Close()

Write-Host ("Found {0}/{1} employees in DB:" -f $empMap.Count, $entries.Count) -ForegroundColor $(if ($empMap.Count -eq $entries.Count) { "Green" } else { "Yellow" })
foreach ($entry in $entries) {
    if ($empMap.ContainsKey($entry.Name)) {
        Write-Host ("  [OK]  {0,-30}  {1}" -f $entry.Name, $empMap[$entry.Name].EmployeeId) -ForegroundColor Green
    } else {
        Write-Host ("  [--]  {0,-30}  NOT FOUND -- will skip" -f $entry.Name) -ForegroundColor Yellow
    }
}

# Görgün special: try the umlaut variant if the ASCII version not found
$goerguns = @("Zehra Sila Görgün","Zehra Sila Goergun","Zehra Sila Gorgün","Zehra Sila Gorgun")
$gorgKey = $null
foreach ($v in $goerguns) {
    if ($empMap.ContainsKey($v)) { $gorgKey = $v; break }
}
if (-not $gorgKey) {
    # Try a LIKE query for the Görgün row
    $likeCmd = New-Object System.Data.SqlClient.SqlCommand(
        "SELECT TOP 1 EmployeeId, FullName, FirstName, LastName, TeamLeadName FROM Employees WHERE FullName LIKE N'Zehra Sila G%rg%n'",
        $conn)
    $lr = $likeCmd.ExecuteReader()
    if ($lr.Read()) {
        $fn = ([string]$lr["FullName"]).Trim()
        $empMap[$fn] = @{
            EmployeeId   = [string]$lr["EmployeeId"]
            FirstName    = [string]$lr["FirstName"]
            LastName     = [string]$lr["LastName"]
            TeamLeadName = [string]$lr["TeamLeadName"]
        }
        # patch the entry to use the DB spelling
        foreach ($e in $entries) {
            if ($e.Name -like "Zehra Sila G*rg*n") { $e.Name = $fn; break }
        }
        Write-Host ("  [OK]  Zehra Sila Gorg/Görgün resolved to '{0}' ({1})" -f $fn, $empMap[$fn].EmployeeId) -ForegroundColor Green
    }
    $lr.Close()
}

# ── Step 2: INSERT ────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Step 2: INSERT into SickLeaves ---" -ForegroundColor Cyan

$insertSql = @"
INSERT INTO SickLeaves
    (EmployeeId, FirstName, LastName, TeamLeadName,
     FirstDay, LastDay, DurationDays, LeaveType, CreatedAt)
VALUES
    (@EmployeeId, @FirstName, @LastName, @TeamLeadName,
     @FirstDay, @LastDay, @DurationDays, @LeaveType, @CreatedAt)
"@

$inserted = 0
$skipped  = 0
$failed   = 0

foreach ($entry in $entries) {
    $emp = $empMap[$entry.Name]
    if (-not $emp) {
        Write-Host ("  SKIP  {0} -- not found in Employees" -f $entry.Name) -ForegroundColor Yellow
        $skipped++
        continue
    }

    $fd      = [datetime]::Parse($entry.FirstDay)
    $ld      = [datetime]::Parse($entry.LastDay)
    $durDays = ($ld - $fd).Days + 1

    try {
        $cmd = New-Object System.Data.SqlClient.SqlCommand($insertSql, $conn)
        $cmd.Parameters.AddWithValue("@EmployeeId",   $(if ($emp.EmployeeId)   { $emp.EmployeeId }   else { [DBNull]::Value })) | Out-Null
        $cmd.Parameters.AddWithValue("@FirstName",    $(if ($emp.FirstName)    { $emp.FirstName }    else { [DBNull]::Value })) | Out-Null
        $cmd.Parameters.AddWithValue("@LastName",     $(if ($emp.LastName)     { $emp.LastName }     else { [DBNull]::Value })) | Out-Null
        $cmd.Parameters.AddWithValue("@TeamLeadName", $(if ($emp.TeamLeadName) { $emp.TeamLeadName } else { [DBNull]::Value })) | Out-Null
        $cmd.Parameters.AddWithValue("@FirstDay",     $fd)              | Out-Null
        $cmd.Parameters.AddWithValue("@LastDay",      $ld)              | Out-Null
        $cmd.Parameters.AddWithValue("@DurationDays", $durDays)         | Out-Null
        $cmd.Parameters.AddWithValue("@LeaveType",    $entry.LeaveType) | Out-Null
        $cmd.Parameters.AddWithValue("@CreatedAt",    [datetime]::UtcNow) | Out-Null
        $cmd.ExecuteNonQuery() | Out-Null

        Write-Host ("  INSERT  {0,-30}  {1}  {2} -> {3}" -f $entry.Name, $entry.LeaveType, $entry.FirstDay, $entry.LastDay) -ForegroundColor Green
        $inserted++
    } catch {
        Write-Host ("  ERROR   {0,-30}  {1}" -f $entry.Name, $_) -ForegroundColor Red
        $failed++
    }
}

# ── Verify ────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Verify ---" -ForegroundColor Cyan
$verifySql = "SELECT COUNT(*) FROM SickLeaves WHERE FirstDay >= '2026-06-15' AND FirstDay <= '2026-06-16' AND CreatedAt >= DATEADD(minute,-5,GETUTCDATE())"
$vCmd  = New-Object System.Data.SqlClient.SqlCommand($verifySql, $conn)
$count = $vCmd.ExecuteScalar()
Write-Host ("  Rows inserted this run (±5 min window): {0}" -f $count) -ForegroundColor $(if ($count -eq $inserted) { "Green" } else { "Yellow" })

$conn.Close()

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host ("Inserted: {0}  Skipped: {1}  Errors: {2}" -f $inserted, $skipped, $failed) `
    -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })

if ($skipped -gt 0) {
    Write-Host ""
    Write-Host "Skipped names were not found in Employees.FullName." -ForegroundColor Yellow
    Write-Host "Check spelling or run:" -ForegroundColor Yellow
    Write-Host "  SELECT EmployeeId, FullName FROM Employees WHERE FullName LIKE '%<part of name>%'" -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "NOTE: These rows are in SickLeaves (report page only)." -ForegroundColor DarkCyan
Write-Host "      WIC coverage/forecast reads ShiftEntries.ShiftType -- not affected." -ForegroundColor DarkCyan
Write-Host ""
Write-Host "=== PS1_20 complete ===" -ForegroundColor Green
