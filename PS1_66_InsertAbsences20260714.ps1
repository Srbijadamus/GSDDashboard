# PS1_66_InsertAbsences20260714.ps1
# Inserts planned absences into SickLeaves for 2026-07-14.
#
# Source: Daily Absence Overview 2026-07-14
#   SL:       6  (Sharon Huber, Dominik Bajic, Pascal Dutz, Anisha Nellikka Panikkan, Mark Bachmann, Sebastian Höck)
#   AL:       12 (Duc Quy Huynh, Timon Philippen, Victoria Scholz, Erne Kis, Christian Koch, Abdulrahman Aldera,
#                 Francois Sicot, Kamil Filipowicz, Kavinraj Pathmanathan, Marcus Rusch, Merlin Voss, Viktor Winter)
#   OFF/OL/CD:10 (Eva-Liane Schliwa, Mustafa Deveci, Christian Pastors, Boris Kostov, Kemal Sener,
#                 Veronika Kouwui, Zehra Sila Görgün, Arevig Ketenjian, Krishnendu Das, Yun Hee Oh)
#   UL:        0
#   Night:     1  (Aleksandrina Dencheva -- working night shift, NOT inserted as absence)
#
# NOTE: OFF/OL/CD column = type is "OFF" by default.
#   If some people are specifically OL or CD, update LeaveType below before running.
#
# NOTE: This inserts into SickLeaves (report page only).
#   WIC coverage/forecast reads ShiftEntries.ShiftType -- NOT affected by this script.

$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"

$TODAY = "2026-07-14"

$entries = @(
    # SL -----------------------------------------------------------------------
    [PSCustomObject]@{ Name="Sharon Huber";               LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Dominik Bajic";              LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Pascal Dutz";                LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Anisha Nellikka Panikkan";   LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Mark Bachmann";              LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Sebastian Höck";             LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },

    # AL -----------------------------------------------------------------------
    [PSCustomObject]@{ Name="Duc Quy Huynh";             LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Timon Philippen";            LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Victoria Scholz";            LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Erne Kis";                   LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Christian Koch";             LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Abdulrahman Aldera";         LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Francois Sicot";             LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Kamil Filipowicz";           LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Kavinraj Pathmanathan";      LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Marcus Rusch";               LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Merlin Voss";                LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Viktor Winter";              LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },

    # OFF/OL/CD -- default OFF; change LeaveType if you know the exact type ---
    [PSCustomObject]@{ Name="Eva-Liane Schliwa";          LeaveType="OFF"; FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Mustafa Deveci";             LeaveType="OFF"; FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Christian Pastors";          LeaveType="OFF"; FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Boris Kostov";               LeaveType="OFF"; FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Kemal Sener";                LeaveType="OFF"; FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Veronika Kouwui";            LeaveType="OFF"; FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Zehra Sila Görgün";          LeaveType="OFF"; FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Arevig Ketenjian";           LeaveType="OFF"; FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Krishnendu Das";             LeaveType="OFF"; FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Yun Hee Oh";                 LeaveType="OFF"; FirstDay=$TODAY; LastDay=$TODAY }
)

# ── Connect ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== PS1_66: Insert Absences $TODAY ===" -ForegroundColor Yellow
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

$nameList = ($entries | ForEach-Object { "'" + $_.Name.Replace("'","''") + "'" }) -join ","
$lookupSql = "SELECT EmployeeId, FullName, FirstName, LastName, TeamLeadName FROM Employees WHERE FullName IN ($nameList)"

$lookupCmd = New-Object System.Data.SqlClient.SqlCommand($lookupSql, $conn)
$reader    = $lookupCmd.ExecuteReader()

$empMap = @{}
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
        Write-Host ("  [OK]  {0,-35}  {1}" -f $entry.Name, $empMap[$entry.Name].EmployeeId) -ForegroundColor Green
    } else {
        Write-Host ("  [--]  {0,-35}  NOT FOUND -- will skip" -f $entry.Name) -ForegroundColor Yellow
    }
}

# Umlaut fallback for Zehra Sila Görgün
$goerguns = @("Zehra Sila Görgün","Zehra Sila Goergun","Zehra Sila Gorgün","Zehra Sila Gorgun")
$gorgKey = $null
foreach ($v in $goerguns) {
    if ($empMap.ContainsKey($v)) { $gorgKey = $v; break }
}
if (-not $gorgKey) {
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
        foreach ($e in $entries) {
            if ($e.Name -like "Zehra Sila G*rg*n") { $e.Name = $fn; break }
        }
        Write-Host ("  [OK]  Zehra Sila Gorg/Görgün resolved to '{0}' ({1})" -f $fn, $empMap[$fn].EmployeeId) -ForegroundColor Green
    }
    $lr.Close()
}

# Umlaut fallback for Sebastian Höck
if (-not $empMap.ContainsKey("Sebastian Höck")) {
    $likeCmd2 = New-Object System.Data.SqlClient.SqlCommand(
        "SELECT TOP 1 EmployeeId, FullName, FirstName, LastName, TeamLeadName FROM Employees WHERE FullName LIKE N'Sebastian H%ck'",
        $conn)
    $lr2 = $likeCmd2.ExecuteReader()
    if ($lr2.Read()) {
        $fn2 = ([string]$lr2["FullName"]).Trim()
        $empMap[$fn2] = @{
            EmployeeId   = [string]$lr2["EmployeeId"]
            FirstName    = [string]$lr2["FirstName"]
            LastName     = [string]$lr2["LastName"]
            TeamLeadName = [string]$lr2["TeamLeadName"]
        }
        foreach ($e in $entries) {
            if ($e.Name -like "Sebastian H*ck") { $e.Name = $fn2; break }
        }
        Write-Host ("  [OK]  Sebastian Höck resolved to '{0}' ({1})" -f $fn2, $empMap[$fn2].EmployeeId) -ForegroundColor Green
    }
    $lr2.Close()
}

# ── Step 2: Duplicate check ───────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Step 1b: Duplicate check (already in SickLeaves for $TODAY) ---" -ForegroundColor Cyan

$dupSql = "SELECT EmployeeId FROM SickLeaves WHERE FirstDay = '$TODAY' AND LastDay = '$TODAY'"
$dupCmd  = New-Object System.Data.SqlClient.SqlCommand($dupSql, $conn)
$dupReader = $dupCmd.ExecuteReader()
$alreadyIn = @{}
while ($dupReader.Read()) { $alreadyIn[[string]$dupReader["EmployeeId"]] = $true }
$dupReader.Close()

if ($alreadyIn.Count -gt 0) {
    Write-Host ("  WARNING: {0} employee(s) already have a SickLeave row for $TODAY -- they will be SKIPPED." -f $alreadyIn.Count) -ForegroundColor Yellow
} else {
    Write-Host "  No duplicates found -- clean to insert." -ForegroundColor Green
}

# ── Step 3: INSERT ────────────────────────────────────────────────────────────

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

    if ($alreadyIn.ContainsKey($emp.EmployeeId)) {
        Write-Host ("  SKIP  {0,-35}  already exists for $TODAY" -f $entry.Name) -ForegroundColor DarkYellow
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

        Write-Host ("  INSERT  {0,-35}  {1}  {2}" -f $entry.Name, $entry.LeaveType, $entry.FirstDay) -ForegroundColor Green
        $inserted++
    } catch {
        Write-Host ("  ERROR   {0,-35}  {1}" -f $entry.Name, $_) -ForegroundColor Red
        $failed++
    }
}

# ── Verify ────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Verify ---" -ForegroundColor Cyan
$verifySql = "SELECT COUNT(*) FROM SickLeaves WHERE FirstDay = '$TODAY' AND CreatedAt >= DATEADD(minute,-5,GETUTCDATE())"
$vCmd  = New-Object System.Data.SqlClient.SqlCommand($verifySql, $conn)
$count = $vCmd.ExecuteScalar()
Write-Host ("  Rows inserted this run (±5 min window): {0}" -f $count) -ForegroundColor $(if ($count -eq $inserted) { "Green" } else { "Yellow" })

$conn.Close()

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host ("=== SUMMARY: Inserted={0}  Skipped={1}  Errors={2} ===" -f $inserted, $skipped, $failed) `
    -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host ""
Write-Host "Breakdown:"
Write-Host "  SL  = 6  (Sharon Huber, Dominik Bajic, Pascal Dutz, Anisha Nellikka Panikkan, Mark Bachmann, Sebastian Höck)"
Write-Host "  AL  = 12 (Duc Quy Huynh, Timon Philippen, Victoria Scholz, Erne Kis, Christian Koch, Abdulrahman Aldera,"
Write-Host "            Francois Sicot, Kamil Filipowicz, Kavinraj Pathmanathan, Marcus Rusch, Merlin Voss, Viktor Winter)"
Write-Host "  OFF = 10 (Eva-Liane Schliwa, Mustafa Deveci, Christian Pastors, Boris Kostov, Kemal Sener,"
Write-Host "            Veronika Kouwui, Zehra Sila Görgün, Arevig Ketenjian, Krishnendu Das, Yun Hee Oh)"
Write-Host "  Night shift (NOT inserted): Aleksandrina Dencheva"
Write-Host ""

if ($skipped -gt 0) {
    Write-Host "Skipped names not found in Employees -- check spelling:" -ForegroundColor Yellow
    Write-Host "  SELECT EmployeeId, FullName FROM Employees WHERE FullName LIKE '%<name>%'" -ForegroundColor DarkYellow
    Write-Host ""
}

Write-Host "NOTE: SickLeaves affects the Sick Leave report page only." -ForegroundColor DarkCyan
Write-Host "      WIC coverage/forecast reads ShiftEntries.ShiftType -- NOT affected." -ForegroundColor DarkCyan
Write-Host ""
Write-Host "=== PS1_66 complete ===" -ForegroundColor Green
