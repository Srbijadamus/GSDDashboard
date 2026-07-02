$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=True;TrustServerCertificate=True"
$conn.Open()

Write-Host "=== Essen-TK MAIN agents: WicShiftEntries + ShiftEntries for 2026-06-22 ===" -ForegroundColor Yellow

$sql = @"
SELECT
    e.EmployeeId,
    e.FullName,
    e.PrimaryRole,
    waa.AssignmentType,
    w.IsOnSite,
    w.SupportLocation        AS WicSupportLocation,
    se.ShiftType,
    se.ShiftStart,
    se.ShiftEnd,
    sl.FirstDay              AS SickFrom,
    sl.LastDay               AS SickTo
FROM WicAgentAssignments waa
JOIN Employees e ON e.EmployeeId = waa.EmployeeId
LEFT JOIN WicShiftEntries w
    ON w.EmployeeId = e.EmployeeId
   AND w.ShiftDate  = '2026-06-22'
LEFT JOIN ShiftEntries se
    ON se.EmployeeId = e.EmployeeId
   AND se.ShiftDate  = '2026-06-22'
LEFT JOIN SickLeaves sl
    ON sl.EmployeeId = e.EmployeeId
   AND sl.FirstDay  <= '2026-06-22'
   AND sl.LastDay   >= '2026-06-22'
WHERE waa.LocationCode = 'DE~45143~Essen~ThyssenKrupp Allee 1'
ORDER BY waa.AssignmentType, e.FullName
"@

$cmd = $conn.CreateCommand()
$cmd.CommandText = $sql
$rdr = $cmd.ExecuteReader()

$rows = @()
while ($rdr.Read()) {
    $rows += [PSCustomObject]@{
        EmployeeId        = $rdr["EmployeeId"]
        FullName          = $rdr["FullName"]
        PrimaryRole       = $rdr["PrimaryRole"]
        AssignmentType    = $rdr["AssignmentType"]
        IsOnSite          = if ($rdr.IsDBNull($rdr.GetOrdinal("IsOnSite"))) { "NULL" } else { $rdr["IsOnSite"] }
        WicSupportLocation= if ($rdr.IsDBNull($rdr.GetOrdinal("WicSupportLocation"))) { "NULL" } else { $rdr["WicSupportLocation"] }
        ShiftType         = if ($rdr.IsDBNull($rdr.GetOrdinal("ShiftType"))) { "NULL" } else { $rdr["ShiftType"] }
        ShiftStart        = if ($rdr.IsDBNull($rdr.GetOrdinal("ShiftStart"))) { "NULL" } else { $rdr["ShiftStart"] }
        ShiftEnd          = if ($rdr.IsDBNull($rdr.GetOrdinal("ShiftEnd"))) { "NULL" } else { $rdr["ShiftEnd"] }
        SickFrom          = if ($rdr.IsDBNull($rdr.GetOrdinal("SickFrom"))) { "" } else { $rdr["SickFrom"] }
        SickTo            = if ($rdr.IsDBNull($rdr.GetOrdinal("SickTo"))) { "" } else { $rdr["SickTo"] }
    }
}
$rdr.Close()

$rows | Format-Table -AutoSize

Write-Host ""
Write-Host "=== WicShiftEntries with SupportLocation LIKE '%Essen%' on 2026-06-22 ===" -ForegroundColor Yellow

$sql2 = @"
SELECT w.EmployeeId, e.FullName, w.SupportLocation, w.IsOnSite, w.ShiftDate
FROM WicShiftEntries w
LEFT JOIN Employees e ON e.EmployeeId = w.EmployeeId
WHERE w.ShiftDate = '2026-06-22'
  AND w.SupportLocation LIKE '%Essen%'
ORDER BY w.SupportLocation, e.FullName
"@

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = $sql2
$rdr2 = $cmd2.ExecuteReader()

$rows2 = @()
while ($rdr2.Read()) {
    $rows2 += [PSCustomObject]@{
        EmployeeId        = $rdr2["EmployeeId"]
        FullName          = if ($rdr2.IsDBNull($rdr2.GetOrdinal("FullName"))) { "?" } else { $rdr2["FullName"] }
        SupportLocation   = $rdr2["SupportLocation"]
        IsOnSite          = $rdr2["IsOnSite"]
        ShiftDate         = $rdr2["ShiftDate"]
    }
}
$rdr2.Close()

if ($rows2.Count -eq 0) {
    Write-Host "  No WicShiftEntries found for any Essen location on 2026-06-22!" -ForegroundColor Red
} else {
    $rows2 | Format-Table -AutoSize
}

$conn.Close()
