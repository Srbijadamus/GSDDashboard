# PS1_29_VWICUpdates.ps1
# 1. INSERT Marko Bosnjak
# 2. UPDATE SecondaryRole = VWIC for 14 agents
# 3. UPDATE Amani Kedo PrimaryRole = VWIC

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)

function Exec($conn, $sql) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    return $cmd.ExecuteNonQuery()
}

try {
    $conn.Open()
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Yellow
    Write-Host "  PS1_29: VWIC Role Updates" -ForegroundColor Yellow
    Write-Host "==========================================" -ForegroundColor Yellow

    # ── 1. INSERT Marko Bosnjak ──────────────────────────────────────────────────
    Write-Host ""
    Write-Host "1. INSERT Marko Bosnjak (9133999)" -ForegroundColor Cyan

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "IF NOT EXISTS (SELECT 1 FROM Employees WHERE EmployeeId = @id) " +
                       "INSERT INTO Employees (EmployeeId, FullName, PrimaryRole, SecondaryRole, TeamLeadName, IsActive) " +
                       "VALUES (@id, @name, @primary, @secondary, @tl, 1)"
    $cmd.Parameters.AddWithValue("@id",        "9133999")           | Out-Null
    $cmd.Parameters.AddWithValue("@name",      "Marko Bosnjak")     | Out-Null
    $cmd.Parameters.AddWithValue("@primary",   "Chat")              | Out-Null
    $cmd.Parameters.AddWithValue("@secondary", "Chat")              | Out-Null
    $cmd.Parameters.AddWithValue("@tl",        "Karlo Coric")       | Out-Null
    $n = $cmd.ExecuteNonQuery()
    if ($n -gt 0) { Write-Host "  Inserted Marko Bosnjak." -ForegroundColor Green }
    else          { Write-Host "  Already exists - skipped." -ForegroundColor DarkYellow }

    # ── 2. UPDATE SecondaryRole = VWIC for 14 agents ────────────────────────────
    Write-Host ""
    Write-Host "2. UPDATE SecondaryRole to VWIC" -ForegroundColor Cyan

    $secondaryIds = @(
        @{ Id = "9074375"; Name = "Elena Schlosser" },
        @{ Id = "9074373"; Name = "Duc Quy Huynh" },
        @{ Id = "9074341"; Name = "Anas Daba" },
        @{ Id = "4451025"; Name = "Benjamin Bitz" },
        @{ Id = "4451022"; Name = "Marcel Marc Bronheim" },
        @{ Id = "9078602"; Name = "Tim Nguyen" },
        @{ Id = "9074535"; Name = "Lubomir Stoyanov" },
        @{ Id = "9074592"; Name = "Victoria Scholz" },
        @{ Id = "9126877"; Name = "Ahmed Hasanovic" },
        @{ Id = "9074381"; Name = "Eva-Liane Schliwa" },
        @{ Id = "3193180"; Name = "Cortneigh Halim" },
        @{ Id = "3193174"; Name = "Anifa Ngcongo" },
        @{ Id = "3193178"; Name = "Samantha Buys" },
        @{ Id = "9085123"; Name = "Anil Bedzeti" }
    )

    foreach ($agent in $secondaryIds) {
        $cmd2 = $conn.CreateCommand()
        $cmd2.CommandText = "UPDATE Employees SET SecondaryRole = 'VWIC' WHERE EmployeeId = @id"
        $cmd2.Parameters.AddWithValue("@id", $agent.Id) | Out-Null
        $n2 = $cmd2.ExecuteNonQuery()
        $status = if ($n2 -gt 0) { "OK" } else { "NOT FOUND" }
        $color  = if ($n2 -gt 0) { "Green" } else { "Red" }
        Write-Host ("  {0,-10}  {1,-30}  SecondaryRole -> VWIC  [{2}]" -f $agent.Id, $agent.Name, $status) -ForegroundColor $color
    }

    # ── 3. UPDATE Amani Kedo PrimaryRole = VWIC ─────────────────────────────────
    Write-Host ""
    Write-Host "3. UPDATE Amani Kedo (9120970) PrimaryRole to VWIC" -ForegroundColor Cyan

    $cmd3 = $conn.CreateCommand()
    $cmd3.CommandText = "UPDATE Employees SET PrimaryRole = 'VWIC' WHERE EmployeeId = @id"
    $cmd3.Parameters.AddWithValue("@id", "9120970") | Out-Null
    $n3 = $cmd3.ExecuteNonQuery()
    if ($n3 -gt 0) { Write-Host "  Amani Kedo PrimaryRole -> VWIC  [OK]" -ForegroundColor Green }
    else           { Write-Host "  9120970 not found."                     -ForegroundColor Red }

    # ── 4. Verify all 19 rows ────────────────────────────────────────────────────
    Write-Host ""
    Write-Host "4. Verification - all 19 rows" -ForegroundColor Cyan

    $cmd4 = $conn.CreateCommand()
    $cmd4.CommandText = "SELECT EmployeeId, FullName, PrimaryRole, SecondaryRole, CAST(IsActive AS INT) AS IsActive " +
                        "FROM Employees " +
                        "WHERE EmployeeId IN (" +
                        "'9074375','9075030','9074373','9074341','4451025','4451022'," +
                        "'9078602','9074535','9085123','9074592','9126877','9120970'," +
                        "'3193180','3193174','3193175','3193178','3193177','9074381','9133999'" +
                        ") ORDER BY FullName"
    $r = $cmd4.ExecuteReader()
    Write-Host ("  {0,-12}  {1,-30}  {2,-10}  {3,-12}  {4}" -f "EmpId","FullName","PrimaryRole","SecondaryRole","Active") -ForegroundColor Yellow
    Write-Host ("  " + "-" * 78) -ForegroundColor DarkGray
    while ($r.Read()) {
        $secRole = if ($r.IsDBNull(3)) { "NULL" } else { $r["SecondaryRole"].ToString() }
        $secColor = if ($secRole -eq "VWIC" -or $r["PrimaryRole"].ToString() -eq "VWIC") { "Green" } else { "White" }
        Write-Host ("  {0,-12}  {1,-30}  {2,-10}  {3,-12}  {4}" -f `
            $r["EmployeeId"], $r["FullName"], $r["PrimaryRole"], $secRole, $r["IsActive"]) -ForegroundColor $secColor
    }
    $r.Close()

} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
} finally {
    $conn.Close()
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Yellow
Write-Host "  PS1_29 complete" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow
