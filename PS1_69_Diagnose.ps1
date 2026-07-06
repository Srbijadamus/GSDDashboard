# PS1_69_Diagnose.ps1  -- READ-ONLY
# Diagnoses why /api/wic/open?date=2026-07-06 appears to return 0 on-site agents.
# Tests 4 hypotheses in order. No DB writes.

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"
$baseUrl         = "http://localhost:5000"
$shiftDate       = "2026-07-06"
$dow             = 1   # DayOfWeek=1 = Monday (0=Sun...6=Sat  per .NET/SQL)

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

function Invoke-Scalar([string]$sql, [hashtable]$params = @{}) {
    $conn = Open-Conn
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($kv in $params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        $v = $cmd.ExecuteScalar()
        if ($v -is [System.DBNull]) { return $null }
        return $v
    } finally { $conn.Close() }
}

Write-Host ""
Write-Host "=== PS1_69 Read-Only Diagnosis for /api/wic/open?date=$shiftDate ===" -ForegroundColor Yellow
Write-Host ""

# ============================================================================================
# HYPOTHESIS 1 — Smoke-check bug in PS1_68 (code-level finding, no DB query needed)
# ============================================================================================
Write-Host "--- HYPOTHESIS 1: PS1_68 smoke-check always returns 0 (code-level finding) ---" -ForegroundColor Cyan
Write-Host ""
Write-Host "  WicShiftService.GetOpenAsync returns: List<WicOpenDayDto>" -ForegroundColor White
Write-Host "  WicOpenDayDto fields: date (string), dayOfWeek (string), locations (List<WicDayStatusDto>)" -ForegroundColor White
Write-Host "  WicDayStatusDto fields: locationCode, displayName, isOpen, coverageStatus, scheduledCount," -ForegroundColor White
Write-Host "                          absentCount, effectiveCoverage, minRequired  -- NO isOnSite field." -ForegroundColor White
Write-Host ""
Write-Host "  PS1_68 smoke-check line:" -ForegroundColor White
Write-Host '    $onSite = @($json | Where-Object { $_.isOnSite -eq $true })' -ForegroundColor Yellow
Write-Host ""
Write-Host "  BUG: `$json is an array of WicOpenDayDto (day-level objects, not agent-level)." -ForegroundColor Red
Write-Host "       None of them have an isOnSite property." -ForegroundColor Red
Write-Host '       $_.isOnSite is always $null; ($null -eq $true) = $false.' -ForegroundColor Red
Write-Host "       The count is ALWAYS 0 regardless of what the API actually returned." -ForegroundColor Red
Write-Host ""
Write-Host "  VERDICT: The '0 on-site agents' message from PS1_68 is a FALSE ALARM." -ForegroundColor Green
Write-Host "           It does NOT mean the API returned no data." -ForegroundColor Green
Write-Host ""

# ============================================================================================
# HYPOTHESIS 2 — Group 1 INNER JOIN dropping rows (regression check)
# ============================================================================================
Write-Host "--- HYPOTHESIS 2: INNER JOIN Employees WHERE IsActive=1 dropping rows ---" -ForegroundColor Cyan

$raw = [int](Invoke-Scalar (
    "SELECT COUNT(*) FROM WicShiftEntries WHERE ShiftDate = @d AND IsOnSite = 1") @{ d = $shiftDate })

$joined = [int](Invoke-Scalar (
    "SELECT COUNT(*) FROM WicShiftEntries w " +
    "INNER JOIN Employees e ON w.EmployeeId = e.EmployeeId AND e.IsActive = 1 " +
    "WHERE w.ShiftDate = @d AND w.IsOnSite = 1") @{ d = $shiftDate })

$dropped = $raw - $joined

Write-Host ("  Raw IsOnSite=1 rows for {0}  : {1}" -f $shiftDate, $raw) -ForegroundColor White
Write-Host ("  Survive INNER JOIN IsActive=1: {0}" -f $joined) -ForegroundColor White
Write-Host ("  Dropped by JOIN              : {0}" -f $dropped) -ForegroundColor White

if ($dropped -eq 0) {
    Write-Host "  VERDICT: JOIN drops 0 rows. Group 1 fix is NOT a regression here." -ForegroundColor Green
} else {
    Write-Host ("  VERDICT: JOIN drops {0} row(s). These EmployeeIds have no IsActive=1 match." -f $dropped) -ForegroundColor Red
    $orphans = Invoke-Rows (
        "SELECT w.EmployeeId, w.SupportLocation, " +
        "       ISNULL(e.FullName,'NOT IN Employees') AS FullName, " +
        "       ISNULL(CAST(e.IsActive AS varchar),'NULL') AS IsActive " +
        "FROM WicShiftEntries w " +
        "LEFT JOIN Employees e ON w.EmployeeId = e.EmployeeId " +
        "WHERE w.ShiftDate = @d AND w.IsOnSite = 1 " +
        "  AND (e.EmployeeId IS NULL OR e.IsActive = 0)") @{ d = $shiftDate }
    foreach ($r in $orphans) {
        Write-Host ("    EmpId={0}  Loc={1}  Name={2}  IsActive={3}" -f `
            $r.EmployeeId, $r.SupportLocation, $r.FullName, $r.IsActive) -ForegroundColor Yellow
    }
}
Write-Host ""

# ============================================================================================
# HYPOTHESIS 3 — Monday opening hours (DayOfWeek=1) present for WIC locations?
# ============================================================================================
Write-Host "--- HYPOTHESIS 3: Monday (DayOfWeek=1) opening hours for WIC locations ---" -ForegroundColor Cyan

$locCount  = [int](Invoke-Scalar "SELECT COUNT(*) FROM WicLocations WHERE IsActive = 1")
$monClosed = [int](Invoke-Scalar (
    "SELECT COUNT(*) FROM WicLocations l " +
    "LEFT JOIN WicOpeningHours h ON " +
    "    (h.LocationCode = l.LocationCode OR " +
    "     (l.LocationCodeLegacy IS NOT NULL AND h.LocationCode = l.LocationCodeLegacy)) " +
    "    AND h.DayOfWeek = @dow " +
    "WHERE l.IsActive = 1 AND (h.Id IS NULL OR h.IsClosed = 1)") @{ dow = $dow })

$monOpen = $locCount - $monClosed

Write-Host ("  Active WicLocations: {0}" -f $locCount) -ForegroundColor White
Write-Host ("  Have open Monday hours (DayOfWeek=1): {0}" -f $monOpen) -ForegroundColor White
Write-Host ("  No Monday record OR IsClosed=1       : {0}  -> isClosed=true in GetOpenAsync" -f $monClosed) -ForegroundColor White

if ($monClosed -gt 0) {
    Write-Host ""
    Write-Host "  Locations with no/closed Monday hours:" -ForegroundColor Yellow
    $closedLocs = Invoke-Rows (
        "SELECT l.LocationCode, l.DisplayName, l.City, " +
        "       ISNULL(CAST(h.IsClosed AS varchar),'NO RECORD') AS MondayStatus " +
        "FROM WicLocations l " +
        "LEFT JOIN WicOpeningHours h ON " +
        "    (h.LocationCode = l.LocationCode OR " +
        "     (l.LocationCodeLegacy IS NOT NULL AND h.LocationCode = l.LocationCodeLegacy)) " +
        "    AND h.DayOfWeek = @dow " +
        "WHERE l.IsActive = 1 AND (h.Id IS NULL OR h.IsClosed = 1) " +
        "ORDER BY l.DisplayName") @{ dow = $dow }
    foreach ($l in $closedLocs) {
        Write-Host ("    {0,-28} {1,-20} Monday={2}" -f $l.DisplayName, $l.City, $l.MondayStatus) -ForegroundColor Yellow
    }
}
Write-Host ""
Write-Host "  NOTE: isClosed=true affects coverageStatus ('CLOSED') and IsOpen=false in the DTO." -ForegroundColor DarkGray
Write-Host "        scheduledCount and effectiveCoverage are computed BEFORE the isClosed check" -ForegroundColor DarkGray
Write-Host "        and will be non-zero for locations with assigned agents." -ForegroundColor DarkGray
Write-Host ""

# ============================================================================================
# HYPOTHESIS 4 — Where does 44 become 0? Trace each step of GetOpenAsync's query chain
# ============================================================================================
Write-Host "--- HYPOTHESIS 4: Step-by-step GetOpenAsync query trace ---" -ForegroundColor Cyan

# Step A: What wicEntries loads (WicShiftEntries + INNER JOIN Employees IsActive=1)
$step_a = Invoke-Rows (
    "SELECT w.EmployeeId, w.SupportLocation, w.IsOnSite, " +
    "       CONVERT(varchar(10), w.ShiftDate, 120) AS ShiftDate " +
    "FROM WicShiftEntries w " +
    "INNER JOIN Employees e ON w.EmployeeId = e.EmployeeId AND e.IsActive = 1 " +
    "WHERE w.ShiftDate = @d") @{ d = $shiftDate }
Write-Host ("  [Step A] wicEntries after INNER JOIN (all rows, any IsOnSite): {0}" -f $step_a.Count) -ForegroundColor White

$step_a_onsite = @($step_a | Where-Object { $_.IsOnSite })
Write-Host ("  [Step A] wicEntries with IsOnSite=1                          : {0}" -f $step_a_onsite.Count) -ForegroundColor White

# Step B: dayWicIds (all IsOnSite=1 EmployeeIds for the date - used for absence check)
$dayWicIdCount = ($step_a_onsite | Select-Object -ExpandProperty EmployeeId -Unique).Count
Write-Host ("  [Step B] dayWicIds (distinct EmployeeIds with IsOnSite=1)    : {0}" -f $dayWicIdCount) -ForegroundColor White

# Step C: absence check - which of those EmployeeIds are absent?
$absenceTypes  = "'SL','AL','UL','PH','LPH','RESIGNED'"
$absentIds     = @()
if ($step_a_onsite.Count -gt 0) {
    $empIds = "'" + (($step_a_onsite | Select-Object -ExpandProperty EmployeeId -Unique) -join "','") + "'"
    $absentRows = Invoke-Rows (
        "SELECT s.EmployeeId, s.ShiftType FROM ShiftEntries s " +
        "WHERE s.ShiftDate = '$shiftDate' AND s.EmployeeId IN ($empIds) " +
        "AND s.ShiftType IN ($absenceTypes)")
    $sickRows = Invoke-Rows (
        "SELECT s.EmployeeId FROM SickLeaves s " +
        "WHERE s.EmployeeId IN ($empIds) " +
        "AND s.FirstDay <= '$shiftDate' AND s.LastDay >= '$shiftDate'")
    $absentIds = @($absentRows.EmployeeId) + @($sickRows.EmployeeId) | Select-Object -Unique
}
Write-Host ("  [Step C] Absent EmployeeIds (SickLeave or absence ShiftType) : {0}" -f $absentIds.Count) -ForegroundColor White
if ($absentIds.Count -gt 0) {
    foreach ($aid in $absentIds) {
        Write-Host ("    Absent: {0}" -f $aid) -ForegroundColor Yellow
    }
}

# Step D: per-location - how many IsOnSite=1 entries match a WIC location's DisplayName
Write-Host ""
Write-Host "  [Step D] Per-location MatchesSupportLocation (stored SupportLocation vs DisplayName):" -ForegroundColor White
$locMatches = Invoke-Rows (
    "SELECT l.LocationCode, l.DisplayName, COUNT(w.Id) AS ScheduledCount " +
    "FROM WicLocations l " +
    "LEFT JOIN WicShiftEntries w " +
    "    ON w.SupportLocation = l.DisplayName  -- exact ordinal match (mirrors MatchesSupportLocation line 72) " +
    "    AND w.ShiftDate = @d " +
    "    AND w.IsOnSite = 1 " +
    "LEFT JOIN Employees e ON w.EmployeeId = e.EmployeeId AND e.IsActive = 1 " +
    "WHERE l.IsActive = 1 " +
    "GROUP BY l.LocationCode, l.DisplayName " +
    "HAVING COUNT(w.Id) > 0 " +
    "ORDER BY COUNT(w.Id) DESC") @{ d = $shiftDate }

if ($locMatches.Count -eq 0) {
    Write-Host "    *** 0 locations have matching WicShiftEntries via exact DisplayName ***" -ForegroundColor Red
    Write-Host "    This means SupportLocation values stored in WicShiftEntries do NOT match" -ForegroundColor Red
    Write-Host "    the DisplayName in WicLocations (ordinal string comparison)." -ForegroundColor Red
    Write-Host "    Check below for actual stored SupportLocation values:" -ForegroundColor Red
    $storedLocs = Invoke-Rows (
        "SELECT DISTINCT SupportLocation, COUNT(*) AS Cnt " +
        "FROM WicShiftEntries WHERE ShiftDate = @d AND IsOnSite = 1 " +
        "AND SupportLocation <> 'VWIC' " +
        "GROUP BY SupportLocation ORDER BY SupportLocation") @{ d = $shiftDate }
    foreach ($sl in $storedLocs) {
        # Check if this SupportLocation has an exact match in WicLocations.DisplayName
        $matchCount = [int](Invoke-Scalar (
            "SELECT COUNT(*) FROM WicLocations WHERE IsActive=1 AND DisplayName = @sl") @{ sl = $sl.SupportLocation })
        $matchTag = if ($matchCount -gt 0) { "OK (exact match)" } else { "NO MATCH in WicLocations.DisplayName" }
        Write-Host ("    '{0}'  (Cnt={1})  -> {2}" -f $sl.SupportLocation, $sl.Cnt, $matchTag) -ForegroundColor $(
            if ($matchCount -eq 0) { "Red" } else { "Green" })
    }
} else {
    foreach ($m in $locMatches) {
        Write-Host ("    {0,-28} ScheduledCount={1}" -f $m.DisplayName, $m.ScheduledCount) -ForegroundColor Green
    }
}

# vWIC summary
$vwicCount = [int](Invoke-Scalar (
    "SELECT COUNT(*) FROM WicShiftEntries WHERE ShiftDate = @d AND SupportLocation = 'VWIC' AND IsOnSite = 1") @{ d = $shiftDate })
Write-Host ""
Write-Host ("  [Step D] SupportLocation='VWIC' rows: {0} (by design, never matched to a WicLocation)" -f $vwicCount) -ForegroundColor DarkGray

# Public holiday check
$natHoliday = Invoke-Rows (
    "SELECT HolidayDate, Name FROM PublicHolidays WHERE HolidayDate = @d AND IsNational = 1") @{ d = $shiftDate }
if ($natHoliday.Count -gt 0) {
    Write-Host ("  [Holiday] NATIONAL HOLIDAY on {0}: {1}" -f $shiftDate, $natHoliday[0].Name) -ForegroundColor Red
} else {
    Write-Host ("  [Holiday] No national holiday on {0}." -f $shiftDate) -ForegroundColor Green
}

Write-Host ""

# ============================================================================================
# ACTUAL API RESPONSE — inspect correctly
# ============================================================================================
Write-Host "--- ACTUAL API RESPONSE (correctly parsed) ---" -ForegroundColor Cyan
try {
    $resp = Invoke-WebRequest -Uri "$baseUrl/api/wic/open?date=$shiftDate&horizon=1" -Method GET -TimeoutSec 10 -UseBasicParsing
    if ($resp.StatusCode -eq 200) {
        $json = $resp.Content | ConvertFrom-Json
        $days = @($json)
        Write-Host ("  HTTP 200.  Days returned: {0}" -f $days.Count) -ForegroundColor Green

        foreach ($day in $days) {
            Write-Host ("  Date={0}  DayOfWeek={1}" -f $day.date, $day.dayOfWeek) -ForegroundColor White
            $withAgents = @($day.locations | Where-Object { $_.scheduledCount -gt 0 })
            $openLocs   = @($day.locations | Where-Object { $_.isOpen -eq $true })
            Write-Host ("    isOpen locations     : {0}/{1}" -f $openLocs.Count, $day.locations.Count) -ForegroundColor White
            Write-Host ("    scheduledCount > 0   : {0} locations" -f $withAgents.Count) -ForegroundColor White
            if ($withAgents.Count -gt 0) {
                foreach ($loc in $withAgents) {
                    Write-Host ("      {0,-30} scheduled={1}  effective={2}  status={3}" -f `
                        $loc.displayName, $loc.scheduledCount, $loc.effectiveCoverage, $loc.coverageStatus) -ForegroundColor Green
                }
            } else {
                Write-Host "    *** scheduledCount = 0 for ALL locations ***" -ForegroundColor Red
                Write-Host "    Top 5 CLOSED locations for reference:" -ForegroundColor Yellow
                $closedSample = @($day.locations | Select-Object -First 5)
                foreach ($loc in $closedSample) {
                    Write-Host ("      {0,-30} isOpen={1}  status={2}  scheduled={3}" -f `
                        $loc.displayName, $loc.isOpen, $loc.coverageStatus, $loc.scheduledCount) -ForegroundColor Yellow
                }
            }
        }

        # Reproduce the PS1_68 smoke-check bug explicitly
        Write-Host ""
        Write-Host "  PS1_68 smoke-check reproduction:" -ForegroundColor Magenta
        $bugResult = @($json | Where-Object { $_.isOnSite -eq $true })
        Write-Host ("    [PS1_68 code] `$json | Where-Object { `$_.isOnSite -eq `$true }  -> Count = {0}" -f $bugResult.Count) -ForegroundColor Magenta
        Write-Host "    This is always 0 because WicOpenDayDto has no isOnSite field." -ForegroundColor Magenta

    } else {
        Write-Host ("  HTTP {0} -- unexpected status." -f $resp.StatusCode) -ForegroundColor Red
    }
} catch {
    Write-Host "  Server not reachable. Start the app and re-run." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== PS1_69 Diagnosis complete ===" -ForegroundColor Cyan
Write-Host ""
