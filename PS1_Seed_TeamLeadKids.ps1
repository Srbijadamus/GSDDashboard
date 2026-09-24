# PS1_Seed_TeamLeadKids.ps1 — Fill the six TEAM_LEAD placeholder rows in AppUserRoles
# with the real KIDs (teamleads.md), confirmed in teamleads2.md:
#   - Delia: update Id=5, KEEP DisplayName "Delia Panaitescu" (must match Employees.TeamLeadName)
#   - O7164: used verbatim as given (4 digits — flagged, not "corrected")
#
# Guards per UPDATE (@@ROWCOUNT must be exactly 1, else THROW -> full ROLLBACK):
#   Role = 'TEAM_LEAD' AND DisplayName = <name> AND EmployeeId IS NULL
#   AND Kid IN ('', <newKid>)            -- re-runnable: fresh placeholder OR already seeded
#   AND IsActive IN (0, 1) with Kid='' implying IsActive=0 on a fresh run
# Delia's row is additionally pinned to Id = 5.
#
# Does NOT touch the enforcement flag, does NOT rebuild/restart/deploy.
$ErrorActionPreference = 'Stop'
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"

function Invoke-Sql($sql) {
  $cn = New-Object System.Data.SqlClient.SqlConnection $cs
  $cn.Open()
  $cmd = $cn.CreateCommand()
  $cmd.CommandText = $sql
  [void]$cmd.ExecuteNonQuery()
  $cn.Close()
}
function Run-Q($label, $sql) {
  $cn = New-Object System.Data.SqlClient.SqlConnection $cs
  $cn.Open()
  $cmd = $cn.CreateCommand()
  $cmd.CommandText = $sql
  $da = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
  $dt = New-Object System.Data.DataTable
  [void]$da.Fill($dt)
  $cn.Close()
  Write-Output ("=== " + $label + " ===")
  $dt | Format-Table -AutoSize | Out-String -Width 300 | Write-Output
}

# ---------------------------------------------------------------------------
# 1. Six guarded UPDATEs in ONE transaction, @@ROWCOUNT = 1 asserted per statement
# ---------------------------------------------------------------------------
Invoke-Sql @"
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRANSACTION;

  UPDATE AppUserRoles
  SET Kid = 'T31561', IsActive = 1, UpdatedAt = SYSUTCDATETIME()
  WHERE Role = 'TEAM_LEAD' AND DisplayName = 'Tobias Rossberg'
    AND EmployeeId IS NULL AND Kid IN ('', 'T31561');
  IF @@ROWCOUNT <> 1 THROW 50010, 'Tobias Rossberg: expected 1 row', 1;

  UPDATE AppUserRoles
  SET Kid = 'M73784', IsActive = 1, UpdatedAt = SYSUTCDATETIME()
  WHERE Id = 5 AND Role = 'TEAM_LEAD' AND DisplayName = 'Delia Panaitescu'
    AND EmployeeId IS NULL AND Kid IN ('', 'M73784');
  IF @@ROWCOUNT <> 1 THROW 50011, 'Delia Panaitescu (Id=5): expected 1 row', 1;

  UPDATE AppUserRoles
  SET Kid = 'O7164', IsActive = 1, UpdatedAt = SYSUTCDATETIME()
  WHERE Role = 'TEAM_LEAD' AND DisplayName = 'Oliver Schleusen'
    AND EmployeeId IS NULL AND Kid IN ('', 'O7164');
  IF @@ROWCOUNT <> 1 THROW 50012, 'Oliver Schleusen: expected 1 row', 1;

  UPDATE AppUserRoles
  SET Kid = 'K33261', IsActive = 1, UpdatedAt = SYSUTCDATETIME()
  WHERE Role = 'TEAM_LEAD' AND DisplayName = 'Karlo Coric'
    AND EmployeeId IS NULL AND Kid IN ('', 'K33261');
  IF @@ROWCOUNT <> 1 THROW 50013, 'Karlo Coric: expected 1 row', 1;

  UPDATE AppUserRoles
  SET Kid = 'I15078', IsActive = 1, UpdatedAt = SYSUTCDATETIME()
  WHERE Role = 'TEAM_LEAD' AND DisplayName = 'Ion Ciuceanu'
    AND EmployeeId IS NULL AND Kid IN ('', 'I15078');
  IF @@ROWCOUNT <> 1 THROW 50014, 'Ion Ciuceanu: expected 1 row', 1;

  UPDATE AppUserRoles
  SET Kid = 'J51904', IsActive = 1, UpdatedAt = SYSUTCDATETIME()
  WHERE Role = 'TEAM_LEAD' AND DisplayName = 'Jaroslaw Brzeszkiewicz'
    AND EmployeeId IS NULL AND Kid IN ('', 'J51904');
  IF @@ROWCOUNT <> 1 THROW 50015, 'Jaroslaw Brzeszkiewicz: expected 1 row', 1;

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH
"@
Write-Output "=== Six guarded UPDATEs committed (each asserted @@ROWCOUNT = 1) ==="

# ---------------------------------------------------------------------------
# 2. Post-write verification
# ---------------------------------------------------------------------------
Run-Q "POST: AppUserRoles full contents" "SELECT Id, Kid, Role, EmployeeId, DisplayName, IsActive FROM AppUserRoles ORDER BY Id"

Run-Q "POST: duplicate KIDs in AppUserRoles (must be empty)" @"
SELECT Kid, COUNT(*) AS C FROM AppUserRoles
WHERE Kid <> '' GROUP BY Kid HAVING COUNT(*) > 1
"@

Run-Q "POST: KID collision with Employees Primary/SecondaryKid (must be empty)" @"
SELECT e.EmployeeId, e.FullName, e.PrimaryKid, e.SecondaryKid, e.IsActive
FROM Employees e
WHERE e.PrimaryKid   IN (SELECT Kid FROM AppUserRoles WHERE Kid <> '')
   OR e.SecondaryKid IN (SELECT Kid FROM AppUserRoles WHERE Kid <> '')
"@

Run-Q "POST: DEV + RTM rows intact and active (must be 3 rows, all IsActive=1)" @"
SELECT Id, Kid, Role, EmployeeId, DisplayName, IsActive
FROM AppUserRoles
WHERE Kid IN ('S69307','S60028','Y6525')
"@

Run-Q "POST: TEAM_LEAD rows all seeded and active (must be 6)" @"
SELECT COUNT(*) AS ActiveTeamLeads
FROM AppUserRoles
WHERE Role = 'TEAM_LEAD' AND IsActive = 1 AND Kid <> ''
"@

Write-Output "=== DONE ==="
