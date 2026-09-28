# PS1_CleanClosedDayDuties.ps1
# Brise buduce WIC dezurstva na dane kada je centar zatvoren.
# Pokreni posle svake promene radnog vremena nekog WIC centra.
# Prvo prikaze sta bi obrisao, pa trazi potvrdu.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandTimeout = 120

$sel = @"
FROM ShiftEntries s
JOIN WicAgentAssignments a ON a.EmployeeName = (SELECT FullName FROM Employees e WHERE e.EmployeeId = s.EmployeeId) AND a.IsActive = 1
JOIN WicLocations l ON l.LocationCode = a.LocationCode
JOIN WicOpeningHours h ON h.LocationCode = l.LocationCode
     AND h.DayOfWeek = (DATEPART(weekday, s.ShiftDate) + @@DATEFIRST - 1) % 7
     AND (h.EffectiveFrom IS NULL OR h.EffectiveFrom <= s.ShiftDate)
WHERE s.IsWicDuty = 1
  AND s.ShiftType = 'WIC_DUTY'
  AND s.ShiftDate >= CAST(GETDATE() AS date)
  AND h.IsClosed = 1
  AND s.AgentTask = l.DisplayName
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours h2
      WHERE h2.LocationCode = h.LocationCode
        AND h2.DayOfWeek = h.DayOfWeek
        AND (h2.EffectiveFrom IS NULL OR h2.EffectiveFrom <= s.ShiftDate)
        AND ISNULL(h2.EffectiveFrom,'1900-01-01') > ISNULL(h.EffectiveFrom,'1900-01-01'))
"@

Write-Host "=== Dezurstva na zatvorene dane ===" -ForegroundColor Cyan
$cmd.CommandText = "SELECT COUNT(*) $sel"
$n = $cmd.ExecuteScalar()

if ($n -eq 0) { Write-Host "Nema nista za ciscenje." -ForegroundColor Green; $cn.Close(); return }

Write-Host ("pronadjeno: {0}" -f $n) -ForegroundColor Yellow
$cmd.CommandText = "SELECT s.ShiftDate, DATENAME(weekday,s.ShiftDate), s.EmployeeId, (SELECT FullName FROM Employees e WHERE e.EmployeeId=s.EmployeeId), s.AgentTask $sel ORDER BY s.ShiftDate"
$rd = $cmd.ExecuteReader()
while ($rd.Read()) { Write-Host ("  {0:yyyy-MM-dd} {1} | {2} | {3}" -f $rd[0],$rd[1],$rd[3],$rd[4]) }
$rd.Close()

Write-Host ""
$odg = Read-Host "Obrisati ove redove? (da/ne)"
if ($odg -ne "da") { Write-Host "Prekinuto, nista nije promenjeno." -ForegroundColor Yellow; $cn.Close(); return }

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$cmd.CommandText = "SELECT s.Id AS OrigId, s.EmployeeId, s.ShiftDate, s.ShiftType, s.ShiftStart, s.ShiftEnd, s.AgentTask INTO ShiftEntries_bak_closed_$stamp $sel"
$cmd.ExecuteNonQuery() | Out-Null
Write-Host ("kopija: ShiftEntries_bak_closed_{0}" -f $stamp) -ForegroundColor Cyan

$cmd.CommandText = "DELETE w FROM WicShiftEntries w JOIN ShiftEntries_bak_closed_$stamp b ON b.EmployeeId=w.EmployeeId AND b.ShiftDate=w.ShiftDate"
Write-Host ("WicShiftEntries obrisano: {0}" -f $cmd.ExecuteNonQuery())
$cmd.CommandText = "DELETE s FROM ShiftEntries s JOIN ShiftEntries_bak_closed_$stamp b ON b.OrigId=s.Id"
Write-Host ("ShiftEntries obrisano: {0}" -f $cmd.ExecuteNonQuery())

Write-Host "`nGotovo. Ti agenti sada nemaju nista tog dana - ako treba BO, dodaj kroz Roster Generator." -ForegroundColor Green
$cn.Close()
