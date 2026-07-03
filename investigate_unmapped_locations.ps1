# investigate_unmapped_locations.ps1
# Lists all distinct SupportLocation values in WicShiftEntries that do NOT map
# to any active WicLocation via WicLocationMatcher (DisplayName, City, or alias map).
# Reports row counts and date ranges so the coverage gap can be sized.
#
# Run with: pwsh -File C:\GSDDashboard\investigate_unmapped_locations.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Add-Type -AssemblyName "System.Data"
$conn = New-Object System.Data.SqlClient.SqlConnection($ConnString)
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
-- Part 1: All active WicLocations with their codes (so we can see what IS mapped)
SELECT LocationCode, LocationCodeLegacy, DisplayName, City
FROM   WicLocations
WHERE  IsActive = 1
ORDER BY LocationCode
"@
$r = $cmd.ExecuteReader()
Write-Host ""
Write-Host "=== Active WicLocations ===" -ForegroundColor Cyan
Write-Host ("  {0,-25} {1,-20} {2,-30} {3}" -f "LocationCode","LocationCodeLegacy","DisplayName","City")
Write-Host ("  {0,-25} {1,-20} {2,-30} {3}" -f ("-"*25),("-"*20),("-"*30),("-"*15))
while ($r.Read()) {
    $legacy = if ($r.IsDBNull(1)) { "" } else { "$($r['LocationCodeLegacy'])" }
    $dn     = if ($r.IsDBNull(2)) { "" } else { "$($r['DisplayName'])" }
    $city   = if ($r.IsDBNull(3)) { "" } else { "$($r['City'])" }
    Write-Host ("  {0,-25} {1,-20} {2,-30} {3}" -f $r['LocationCode'], $legacy, $dn, $city)
}
$r.Close()

Write-Host ""
Write-Host "=== SupportLocation values in WicShiftEntries that have NO match ===" -ForegroundColor Yellow
Write-Host "    (checked against: DisplayName, City, and the full alias map)" -ForegroundColor Yellow
Write-Host ""

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = @"
SELECT
    ISNULL(w.SupportLocation, N'(null)') AS SupportLocation,
    COUNT(*)                              AS TotalRows,
    COUNT(CASE WHEN w.IsOnSite = 1 THEN 1 END) AS OnSiteRows,
    COUNT(DISTINCT w.EmployeeId)          AS DistinctEmployees,
    MIN(CONVERT(varchar(10), w.ShiftDate, 23)) AS EarliestDate,
    MAX(CONVERT(varchar(10), w.ShiftDate, 23)) AS LatestDate
FROM WicShiftEntries w
WHERE
    NOT EXISTS (
        SELECT 1 FROM WicLocations wl
        WHERE wl.IsActive = 1
          AND (w.SupportLocation = wl.DisplayName OR w.SupportLocation = wl.City)
    )
    AND NOT EXISTS (
        SELECT 1 FROM (VALUES
            (N'Essen BP1',             N'DE_Essen_BP1'),
            (N'Essen TK1',             N'DE_Essen_TK1'),
            (N'Halle',                 N'DE_Halle'),
            (N'Berlin - Gaussstr',     N'DE_Berlin_Gauss'),
            (N'Furstenwalde',          N'DE_Furstenwalde'),
            (N'Munchen',               N'DE_Munchen'),
            (N'Osnabruck',             N'DE_Osnabruck'),
            (N'Saarbrucken',           N'DE_Saarbrucken'),
            (N'Demmin - Am Hanseufer', N'DE_Demmin_Hanse'),
            (N'Denbosch',              N'NL_Denbosch'),
            (N'Augsburg',              N'DE_Augsburg'),
            (N'Bamberg',               N'DE_Bamberg'),
            (N'Brokdorf',              N'DE_Brokdorf'),
            (N'Dortmund',              N'DE_Dortmund'),
            (N'Emmerthal',             N'DE_Emmerthal'),
            (N'Essenbach',             N'DE_Essenbach'),
            (N'Grafenrheinfeld',       N'DE_Grafenrheinfeld'),
            (N'Hamburg',               N'DE_Hamburg'),
            (N'Hannover',              N'DE_Hannover'),
            (N'Helmstedt',             N'DE_Helmstedt'),
            (N'Neu-Isenburg',          N'DE_NeuIsenburg'),
            (N'Pfaffenhofen',          N'PFAFFENHOFEN'),
            (N'Potsdam',               N'DE_Potsdam'),
            (N'Quickborn',             N'DE_Quickborn'),
            (N'Regensburg',            N'DE_Regensburg'),
            (N'Rendsburg',             N'RENDSBURG'),
            (N'Salzgitter',            N'DE_Salzgitter'),
            (N'Stade',                 N'DE_Stade'),
            (N'Stadland',              N'DE_Stadland'),
            (N'Zwolle',                N'NL_Zwolle'),
            (N'Essen (Bruesseler Pl.)',  N'DE_Essen_BP1'),
            (N'Essen (Br' + NCHAR(252) + N'sseler Pl.)',   N'DE_Essen_BP1'),
            (N'Essen (Brusseler Pl.)',                       N'DE_Essen_BP1'),
            (N'Essen (ThyssenKrupp)',    N'DE_Essen_TK1'),
            (N'Demmin (Am Hanseufer)',   N'DE_Demmin_Hanse'),
            (N'Berlin (Koepenicker)',    N'DE_Berlin_Kopenick'),
            (N'Berlin (K' + NCHAR(246) + N'penicker)',       N'DE_Berlin_Kopenick'),
            (N'Berlin (Kopenicker)',                          N'DE_Berlin_Kopenick'),
            (N'Berlin - Kopenicker',     N'DE_Berlin_Kopenick'),
            (N'Essen_BP1',               N'DE_Essen_BP1'),
            (N'Essen_TK1',               N'DE_Essen_TK1'),
            (N'Demmin_Wold',             N'DE_Demmin_Wold'),
            (N'Demmin_Hanse',            N'DE_Demmin_Hanse')
        ) AS m(sl, lc)
        JOIN WicLocations wl
          ON (wl.LocationCode = m.lc OR wl.LocationCodeLegacy = m.lc)
          AND wl.IsActive = 1
        WHERE m.sl = w.SupportLocation
    )
GROUP BY w.SupportLocation
ORDER BY OnSiteRows DESC, TotalRows DESC
"@
$r2 = $cmd2.ExecuteReader()
Write-Host ("  {0,-35} {1,6} {2,8} {3,9}  {4}  {5}" -f "SupportLocation","Total","OnSite","Employees","EarliestDate","LatestDate")
Write-Host ("  {0,-35} {1,6} {2,8} {3,9}  {4}  {5}" -f ("-"*35),("-"*6),("-"*8),("-"*9),("-"*10),("-"*10))
$unmappedCount = 0
$totalOnSite   = 0
while ($r2.Read()) {
    $unmappedCount++
    $os = [int]$r2['OnSiteRows']
    $totalOnSite += $os
    Write-Host ("  {0,-35} {1,6} {2,8} {3,9}  {4}  {5}" -f `
        $r2['SupportLocation'], $r2['TotalRows'], $os, $r2['DistinctEmployees'],
        $r2['EarliestDate'], $r2['LatestDate'])
}
$r2.Close()

Write-Host ""
Write-Host ("  => {0} distinct unmapped SupportLocation value(s), {1} on-site rows total invisible to coverage." -f $unmappedCount, $totalOnSite) -ForegroundColor $(if ($unmappedCount -eq 0) { "Green" } else { "Red" })

# Part 3: For each unmapped value, show the distinct employees affected
Write-Host ""
Write-Host "=== Employees per unmapped SupportLocation (IsOnSite=1 only) ===" -ForegroundColor Cyan

$cmd3 = $conn.CreateCommand()
$cmd3.CommandText = @"
SELECT
    ISNULL(w.SupportLocation, N'(null)') AS SupportLocation,
    w.EmployeeId,
    e.FullName,
    COUNT(*)                              AS Days,
    MIN(CONVERT(varchar(10), w.ShiftDate, 23)) AS EarliestDate,
    MAX(CONVERT(varchar(10), w.ShiftDate, 23)) AS LatestDate
FROM WicShiftEntries w
LEFT JOIN Employees e ON e.EmployeeId = w.EmployeeId
WHERE w.IsOnSite = 1
    AND NOT EXISTS (
        SELECT 1 FROM WicLocations wl
        WHERE wl.IsActive = 1
          AND (w.SupportLocation = wl.DisplayName OR w.SupportLocation = wl.City)
    )
    AND NOT EXISTS (
        SELECT 1 FROM (VALUES
            (N'Essen BP1',             N'DE_Essen_BP1'),
            (N'Essen TK1',             N'DE_Essen_TK1'),
            (N'Halle',                 N'DE_Halle'),
            (N'Berlin - Gaussstr',     N'DE_Berlin_Gauss'),
            (N'Furstenwalde',          N'DE_Furstenwalde'),
            (N'Munchen',               N'DE_Munchen'),
            (N'Osnabruck',             N'DE_Osnabruck'),
            (N'Saarbrucken',           N'DE_Saarbrucken'),
            (N'Demmin - Am Hanseufer', N'DE_Demmin_Hanse'),
            (N'Denbosch',              N'NL_Denbosch'),
            (N'Augsburg',              N'DE_Augsburg'),
            (N'Bamberg',               N'DE_Bamberg'),
            (N'Brokdorf',              N'DE_Brokdorf'),
            (N'Dortmund',              N'DE_Dortmund'),
            (N'Emmerthal',             N'DE_Emmerthal'),
            (N'Essenbach',             N'DE_Essenbach'),
            (N'Grafenrheinfeld',       N'DE_Grafenrheinfeld'),
            (N'Hamburg',               N'DE_Hamburg'),
            (N'Hannover',              N'DE_Hannover'),
            (N'Helmstedt',             N'DE_Helmstedt'),
            (N'Neu-Isenburg',          N'DE_NeuIsenburg'),
            (N'Pfaffenhofen',          N'PFAFFENHOFEN'),
            (N'Potsdam',               N'DE_Potsdam'),
            (N'Quickborn',             N'DE_Quickborn'),
            (N'Regensburg',            N'DE_Regensburg'),
            (N'Rendsburg',             N'RENDSBURG'),
            (N'Salzgitter',            N'DE_Salzgitter'),
            (N'Stade',                 N'DE_Stade'),
            (N'Stadland',              N'DE_Stadland'),
            (N'Zwolle',                N'NL_Zwolle'),
            (N'Essen (Bruesseler Pl.)',  N'DE_Essen_BP1'),
            (N'Essen (Br' + NCHAR(252) + N'sseler Pl.)',   N'DE_Essen_BP1'),
            (N'Essen (Brusseler Pl.)',                       N'DE_Essen_BP1'),
            (N'Essen (ThyssenKrupp)',    N'DE_Essen_TK1'),
            (N'Demmin (Am Hanseufer)',   N'DE_Demmin_Hanse'),
            (N'Berlin (Koepenicker)',    N'DE_Berlin_Kopenick'),
            (N'Berlin (K' + NCHAR(246) + N'penicker)',       N'DE_Berlin_Kopenick'),
            (N'Berlin (Kopenicker)',                          N'DE_Berlin_Kopenick'),
            (N'Berlin - Kopenicker',     N'DE_Berlin_Kopenick'),
            (N'Essen_BP1',               N'DE_Essen_BP1'),
            (N'Essen_TK1',               N'DE_Essen_TK1'),
            (N'Demmin_Wold',             N'DE_Demmin_Wold'),
            (N'Demmin_Hanse',            N'DE_Demmin_Hanse')
        ) AS m(sl, lc)
        JOIN WicLocations wl
          ON (wl.LocationCode = m.lc OR wl.LocationCodeLegacy = m.lc)
          AND wl.IsActive = 1
        WHERE m.sl = w.SupportLocation
    )
GROUP BY w.SupportLocation, w.EmployeeId, e.FullName
ORDER BY w.SupportLocation, Days DESC
"@
$r3 = $cmd3.ExecuteReader()
$prevLoc = $null
while ($r3.Read()) {
    $loc = "$($r3['SupportLocation'])"
    if ($loc -ne $prevLoc) {
        Write-Host ""
        Write-Host ("  SupportLocation: '{0}'" -f $loc) -ForegroundColor Yellow
        Write-Host ("    {0,-15} {1,-30} {2,5}  {3}  {4}" -f "EmployeeId","FullName","Days","EarliestDate","LatestDate")
        $prevLoc = $loc
    }
    $fn = if ($r3.IsDBNull(2)) { "(no name)" } else { "$($r3['FullName'])" }
    Write-Host ("    {0,-15} {1,-30} {2,5}  {3}  {4}" -f `
        $r3['EmployeeId'], $fn, $r3['Days'], $r3['EarliestDate'], $r3['LatestDate'])
}
$r3.Close()
$conn.Close()

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host ""
