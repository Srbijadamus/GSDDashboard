# PS1_Seed_AppUserRoles.ps1 — Phase 2 step (a): create AppUserRoles, seed DEV row, verify.
# IDEMPOTENT — identical SQL to the startup block in Backend/Program.cs, so running it
# by hand now is exactly what the app will do (and skip) on its next start.
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

# 1. Table (idempotent)
Invoke-Sql @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AppUserRoles')
CREATE TABLE AppUserRoles (
    Id          INT            IDENTITY(1,1) PRIMARY KEY,
    Kid         NVARCHAR(20)   NOT NULL DEFAULT '',
    Role        NVARCHAR(20)   NOT NULL,
    EmployeeId  NVARCHAR(20)   NULL,
    DisplayName NVARCHAR(200)  NOT NULL,
    IsActive    BIT            NOT NULL DEFAULT 1,
    CreatedAt   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt   DATETIME2      NULL
)
"@
Invoke-Sql @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_AppUserRoles_Kid' AND object_id = OBJECT_ID('AppUserRoles'))
    CREATE UNIQUE INDEX UX_AppUserRoles_Kid ON AppUserRoles (Kid) WHERE Kid <> ''
"@

# 2. Seed: DEV + RTMs (idempotent). These KIDs exist in NEITHER Employees.PrimaryKid
#    NOR Employees.SecondaryKid (PS1_AuthVerify.ps1 query 5) — AppUserRoles is their
#    only login path. No fake Employees rows, no KID written into Employees.
Invoke-Sql @"
IF NOT EXISTS (SELECT 1 FROM AppUserRoles WHERE Kid = 'S69307')
    INSERT INTO AppUserRoles (Kid, Role, EmployeeId, DisplayName, IsActive)
    VALUES ('S69307', 'DEV', '9074466', 'Stojnic Nebojsa', 1);
IF NOT EXISTS (SELECT 1 FROM AppUserRoles WHERE Kid = 'S60028')
    INSERT INTO AppUserRoles (Kid, Role, EmployeeId, DisplayName, IsActive)
    VALUES ('S60028', 'RTM', NULL, 'Silvija Angelova', 1);
IF NOT EXISTS (SELECT 1 FROM AppUserRoles WHERE Kid = 'Y6525')
    INSERT INTO AppUserRoles (Kid, Role, EmployeeId, DisplayName, IsActive)
    VALUES ('Y6525', 'RTM', NULL, 'Yiting Qiang', 1);
"@

# 3. TEAM_LEAD placeholders: Kid = '' AND IsActive = 0 → can NEVER authenticate.
#    Fill Kid + IsActive = 1 once the real KIDs are provided.
$teamLeads = @('Tobias Rossberg','Delia Panaitescu','Oliver Schleusen','Karlo Coric','Ion Ciuceanu','Jaroslaw Brzeszkiewicz')
foreach ($name in $teamLeads) {
  Invoke-Sql ("IF NOT EXISTS (SELECT 1 FROM AppUserRoles WHERE Role = 'TEAM_LEAD' AND DisplayName = '{0}') INSERT INTO AppUserRoles (Kid, Role, EmployeeId, DisplayName, IsActive) VALUES ('', 'TEAM_LEAD', NULL, '{0}', 0);" -f $name)
}

# 4. Verify with real queries
Run-Q "AppUserRoles full contents" "SELECT Id, Kid, Role, EmployeeId, DisplayName, IsActive FROM AppUserRoles ORDER BY Id"
Run-Q "Active DEV logins (must be >= 1)" "SELECT COUNT(*) AS ActiveDevLogins FROM AppUserRoles WHERE Role='DEV' AND IsActive=1 AND Kid<>''"
Run-Q "AGENT rows without EmployeeId (must be 0)" "SELECT COUNT(*) AS BadAgentRows FROM AppUserRoles WHERE Role='AGENT' AND (EmployeeId IS NULL OR EmployeeId='')"
Run-Q "DEV row resolves (rule a dry-run)" "SELECT Kid, Role, EmployeeId, DisplayName FROM AppUserRoles WHERE IsActive=1 AND Kid<>'' AND Kid='S69307'"

# 5. Reachability report (odgovor3.md decision 5)
Run-Q "Rule (b) vs (c) reachability" @"
SELECT
  SUM(CASE WHEN (PrimaryKid IS NOT NULL AND PrimaryKid<>'') OR (SecondaryKid IS NOT NULL AND SecondaryKid<>'') THEN 1 ELSE 0 END) AS ReachableByKid_b,
  SUM(CASE WHEN (PrimaryKid IS NULL OR PrimaryKid='') AND (SecondaryKid IS NULL OR SecondaryKid='') THEN 1 ELSE 0 END) AS ReachableByEmployeeId_c,
  COUNT(*) AS ActiveTotal
FROM Employees WHERE IsActive=1
"@

Write-Output "=== DONE ==="
