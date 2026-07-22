# PS1_67_InsertMissingAbsences20260714.ps1
# Inserts the 19 absences still missing from DB for 2026-07-14.
#
# Already confirmed in DB (via "Absent Today" list, 11 entries):
#   Benjamin Bitz, Christian Koch, Duc Quy Huynh, Erne Kis, Ivonne Specht,
#   Marcus Rusch, Merlin Voss, Sebastian Höck, Timon Philippen, Victoria Scholz, Viktor Winter
#
# Missing (19):
#   SL  (5): Sharon Huber, Dominik Bajic, Pascal Dutz, Anisha Nellikka Panikkan, Mark Bachmann
#   AL  (4): Abdulrahman Aldera, Francois Sicot, Kamil Filipowicz, Kavinraj Pathmanathan
#   OFF (10): Eva-Liane Schliwa, Mustafa Deveci, Christian Pastors, Boris Kostov, Kemal Sener,
#             Veronika Kouwui, Zehra Sila Görgün, Arevig Ketenjian, Krishnendu Das, Yun Hee Oh

$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
$TODAY   = "2026-07-14"

$entries = @(
    # SL -----------------------------------------------------------------------
    [PSCustomObject]@{ Name="Sharon Huber";               LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Dominik Bajic";              LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Pascal Dutz";                LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Anisha Nellikka Panikkan";   LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Mark Bachmann";              LeaveType="SL";  FirstDay=$TODAY; LastDay=$TODAY },

    # AL -----------------------------------------------------------------------
    [PSCustomObject]@{ Name="Abdulrahman Aldera";         LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Francois Sicot";             LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Kamil Filipowicz";           LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },
    [PSCustomObject]@{ Name="Kavinraj Pathmanathan";      LeaveType="AL";  FirstDay=$TODAY; LastDay=$TODAY },

    # OFF/OL/CD -- default OFF; update LeaveType if specific type known --------
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
Write-Host "=== PS1_67: Insert Missing Absences $TODAY ===" -ForegroundColor Yellow
Write-Host "    Expected: 5 SL + 4 AL + 10 OFF = 19 entries" -ForegroundColor Yellow
Write-Host ""

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
try { $conn.Open(); Write-Host "DB: OK" -ForegroundColor Green }
catch { Write-Host "ERROR: $_" -ForegroundColor Red; exit 1 }

# ── Step 1: Lookup EmployeeIds ────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Step 1: Lookup EmployeeIds ---" -ForegroundColor Cyan

$nameList  = ($entries | ForEach-Object { "'" + $_.Name.Replace("'","''") + "'" }) -join ","
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

Write-Host ("Found {0}/{1} employees in DB:" -f $empMap.Count, $entries.Count) `
    -ForegroundColor $(if ($empMap.Count -eq $entries.Count) { "Green" } else { "Yellow" })

foreach ($entry in $entries) {
    if ($empMap.ContainsKey($entry.Name)) {
        Write-Host ("  [OK]  {0,-35}  {1}" -f $entry.Name, $empMap[$entry.Name].EmployeeId) -ForegroundColor Green
    } else {
        Write-Host ("  [--]  {0,-35}  NOT FOUND -- will try LIKE fallback" -f $entry.Name) -ForegroundColor Yellow
    }
}

# Umlaut fallbacks
$likeQueries = @(
    @{ Pattern = "Zehra Sila G%rg%n";  EntryMatch = "Zehra Sila G*rg*n" },
    @{ Pattern = "Sebastian H%ck";      EntryMatch = "Sebastian H*ck"    }
)
foreach ($lq in $likeQueries) {
    $matchedEntry = $entries | Where-Object { $_.Name -like $lq.EntryMatch } | Select-Object -First 1
    if ($matchedEntry -and -not $empMap.ContainsKey($matchedEntry.Name)) {
        $likeCmd = New-Object System.Data.SqlClient.SqlCommand(
            ("SELECT TOP 1 EmployeeId, FullName, FirstName, LastName, TeamLeadName FROM Employees WHERE FullName LIKE N'{0}'" -f $lq.Pattern),
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
            $matchedEntry.Name = $fn
            Write-Host ("  [OK]  LIKE resolved: '{0}' -> '{1}' ({2})" -f $lq.EntryMatch, $fn, $empMap[$fn].EmployeeId) -ForegroundColor Green
        }
        $lr.Close()
    }
}

# ── Step 2: Duplicate check ───────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Step 2: Duplicate check ---" -ForegroundColor Cyan

$resolvedIds = ($empMap.Values | ForEach-Object { $_.EmployeeId }) -join ","
$alreadyIn   = @{}

if ($resolvedIds) {
    $dupSql = "SELECT EmployeeId FROM SickLeaves WHERE EmployeeId IN ($resolvedIds) AND FirstDay <= '$TODAY' AND LastDay >= '$TODAY'"
    $dupCmd = New-Object System.Data.SqlClient.SqlCommand($dupSql, $conn)
    $dr     = $dupCmd.ExecuteReader()
    while ($dr.Read()) { $alreadyIn[[string]$dr["EmployeeId"]] = $true }
    $dr.Close()
}

if ($alreadyIn.Count -gt 0) {
    Write-Host ("  {0} employee(s) already covered for $TODAY -- will skip." -f $alreadyIn.Count) -ForegroundColor Yellow
} else {
    Write-Host "  No duplicates -- clean to insert." -ForegroundColor Green
}

# ── Step 3: INSERT ────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Step 3: INSERT ---" -ForegroundColor Cyan

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
        Write-Host ("  SKIP  {0,-35}  not found in Employees" -f $entry.Name) -ForegroundColor Yellow
        $skipped++; continue
    }
    if ($alreadyIn.ContainsKey($emp.EmployeeId)) {
        Write-Host ("  SKIP  {0,-35}  already covered for $TODAY" -f $entry.Name) -ForegroundColor DarkYellow
        $skipped++; continue
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

        Write-Host ("  INSERT  {0,-35}  {1}" -f $entry.Name, $entry.LeaveType) -ForegroundColor Green
        $inserted++
    } catch {
        Write-Host ("  ERROR   {0,-35}  {1}" -f $entry.Name, $_) -ForegroundColor Red
        $failed++
    }
}

# ── Verify ────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "--- Verify ---" -ForegroundColor Cyan
$vSql  = "SELECT COUNT(*) FROM SickLeaves WHERE FirstDay <= '$TODAY' AND LastDay >= '$TODAY'"
$vCmd  = New-Object System.Data.SqlClient.SqlCommand($vSql, $conn)
$total = $vCmd.ExecuteScalar()
Write-Host ("  Total absent today in DB: {0}" -f $total) -ForegroundColor Cyan

$conn.Close()

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host ("=== SUMMARY: Inserted={0}  Skipped={1}  Errors={2} ===" -f $inserted, $skipped, $failed) `
    -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host ""
Write-Host "  Already in DB (11): Benjamin Bitz, Christian Koch, Duc Quy Huynh, Erne Kis," -ForegroundColor DarkCyan
Write-Host "    Ivonne Specht, Marcus Rusch, Merlin Voss, Sebastian Höck," -ForegroundColor DarkCyan
Write-Host "    Timon Philippen, Victoria Scholz, Viktor Winter" -ForegroundColor DarkCyan
Write-Host ""

if ($skipped -gt 0) {
    Write-Host "Skipped names not found -- check spelling:" -ForegroundColor Yellow
    Write-Host "  SELECT EmployeeId, FullName FROM Employees WHERE FullName LIKE '%<name>%'" -ForegroundColor DarkYellow
    Write-Host ""
}

Write-Host "NOTE: SickLeaves = report page only. WIC coverage reads ShiftEntries, not affected." -ForegroundColor DarkCyan
Write-Host ""
Write-Host "=== PS1_67 complete ===" -ForegroundColor Green
