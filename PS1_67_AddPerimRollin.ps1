# PS1_67_AddPerimRollin.ps1
# Adds Perim Rollin (SME, Full Time, 08:00-17:00) to the system.
# Tables touched: Employees, ShiftEntries (2026-07-07..2026-12-31), ALBalance.
#
# Run with: pwsh -File C:\GSDDashboard\PS1_67_AddPerimRollin.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

$EmpId       = "9074559"
$FirstName   = "Perim"
$LastName    = "Rollin"
$FullName    = "Perim Rollin"
$PrimaryKid  = "P37510"
$EonEmail    = "Perim.Rollin.external@eon.com"
$Role        = "SME"
$Engagement  = "Full Time"
$Category    = "Management"
$Bundesland  = "Nordrhein-Westfalen"
$ShiftStart  = "08:00"
$ShiftEnd    = "17:00"
$DateFrom    = [datetime]"2026-07-07"
$DateTo      = [datetime]"2026-12-31"
$SourceSheet  = "GSD_DE"
$SourceModule = "PS1_67"

# NRW public holidays in range that fall on weekdays -> ShiftType PH or LPH
# Oct 3 (Sat), Nov 1 (Sun), Dec 26 (Sat) are already weekends - no correction needed
$NationalPH  = @("2026-12-25")   # 1. Weihnachtstag (Friday)
$RegionalPH  = @()               # Allerheiligen Nov 1 = Sunday

Add-Type -AssemblyName "System.Data"

function New-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection($ConnString)
    $c.Open()
    return $c
}

function Exec-Scalar([System.Data.SqlClient.SqlConnection]$conn, [System.Data.SqlClient.SqlTransaction]$txn, [string]$sql, [hashtable]$p = @{}) {
    $cmd = $conn.CreateCommand()
    if ($txn) { $cmd.Transaction = $txn }
    $cmd.CommandText = $sql
    foreach ($kv in $p.GetEnumerator()) { [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value) }
    $v = $cmd.ExecuteScalar()
    if ($v -is [System.DBNull] -or $null -eq $v) { return 0 } else { return $v }
}

function Exec-NonQuery([System.Data.SqlClient.SqlConnection]$conn, [System.Data.SqlClient.SqlTransaction]$txn, [string]$sql, [hashtable]$p = @{}) {
    $cmd = $conn.CreateCommand()
    if ($txn) { $cmd.Transaction = $txn }
    $cmd.CommandText = $sql
    foreach ($kv in $p.GetEnumerator()) { [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value) }
    return $cmd.ExecuteNonQuery()
}

Write-Host ""
Write-Host "=== PS1_67: Add Perim Rollin (SME, Full Time 08:00-17:00) ===" -ForegroundColor Cyan
Write-Host "  EmployeeId : $EmpId" -ForegroundColor White
Write-Host "  PrimaryKid : $PrimaryKid" -ForegroundColor White
Write-Host "  EonEmail   : $EonEmail" -ForegroundColor White
Write-Host "  Bundesland : $Bundesland" -ForegroundColor White
Write-Host "  Shifts     : $($DateFrom.ToString('yyyy-MM-dd')) .. $($DateTo.ToString('yyyy-MM-dd'))" -ForegroundColor White
Write-Host ""

$Conn = New-Conn
$xact = $Conn.CreateCommand(); $xact.CommandText = "SET XACT_ABORT ON"; [void]$xact.ExecuteNonQuery()
$Txn  = $Conn.BeginTransaction()

try {

    # ------------------------------------------------------------------
    # 1. Employees
    # ------------------------------------------------------------------
    $nEmp = Exec-NonQuery $Conn $Txn @"
IF NOT EXISTS (SELECT 1 FROM Employees WHERE EmployeeId = @id)
INSERT INTO Employees
    (EmployeeId, FirstName, LastName, FullName, PrimaryRole, SecondaryRole,
     Engagement, Category, Bundesland, PrimaryKid, EonEmail,
     IsActive, IsTrainee, SourceSheet, CreatedAt)
VALUES
    (@id, @fn, @ln, @full, @role, @role,
     @eng, @cat, @bl, @kid, @email,
     1, 0, @src, GETDATE())
"@ @{
        id    = $EmpId;    fn    = $FirstName;  ln    = $LastName;  full  = $FullName
        role  = $Role;     eng   = $Engagement; cat   = $Category;  bl    = $Bundesland
        kid   = $PrimaryKid; email = $EonEmail; src   = $SourceSheet
    }

    if ($nEmp -eq 1) {
        Write-Host "  Employees: inserted 1 row" -ForegroundColor Green
    } else {
        Write-Host "  Employees: already exists -- skipped INSERT" -ForegroundColor Yellow
    }

    # ------------------------------------------------------------------
    # 2. ShiftEntries: weekdays = WORKING, weekends = OFF_WEEKEND
    #    NRW public holidays on weekdays -> PH / LPH
    # ------------------------------------------------------------------
    $insertedWorking = 0; $insertedWeekend = 0; $insertedPH = 0; $skipped = 0

    $cur = $DateFrom
    while ($cur -le $DateTo) {
        $dateStr = $cur.ToString("yyyy-MM-dd")
        $isWeekend = $cur.DayOfWeek -in @([DayOfWeek]::Saturday, [DayOfWeek]::Sunday)
        $isNatPH   = $NationalPH  -contains $dateStr
        $isRegPH   = $RegionalPH  -contains $dateStr

        if ($isNatPH) {
            $sType = "PH";          $sStart = [DBNull]::Value; $sEnd = [DBNull]::Value
        } elseif ($isRegPH) {
            $sType = "LPH";         $sStart = [DBNull]::Value; $sEnd = [DBNull]::Value
        } elseif ($isWeekend) {
            $sType = "OFF_WEEKEND"; $sStart = [DBNull]::Value; $sEnd = [DBNull]::Value
        } else {
            $sType = "WORKING";     $sStart = $ShiftStart;     $sEnd = $ShiftEnd
        }

        $n = Exec-NonQuery $Conn $Txn @"
IF NOT EXISTS (SELECT 1 FROM ShiftEntries WHERE EmployeeId=@id AND ShiftDate=@d AND SourceSheet=@src)
INSERT INTO ShiftEntries
    (EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, IsWicDuty, AutoGenerated, SourceModule, SourceSheet)
VALUES
    (@id, @d, @stype, @sstart, @send, 0, 0, @mod, @src)
"@ @{ id=$EmpId; d=$dateStr; stype=$sType; sstart=$sStart; send=$sEnd; mod=$SourceModule; src=$SourceSheet }

        if ($n -eq 1) {
            if ($sType -in @("PH","LPH")) { $insertedPH++ }
            elseif ($isWeekend) { $insertedWeekend++ }
            else { $insertedWorking++ }
        } else { $skipped++ }

        $cur = $cur.AddDays(1)
    }

    Write-Host ("  ShiftEntries: {0} WORKING + {1} OFF_WEEKEND + {2} PH/LPH inserted  ({3} skipped)" -f `
        $insertedWorking, $insertedWeekend, $insertedPH, $skipped) -ForegroundColor Green

    # ------------------------------------------------------------------
    # 3. ALBalance
    # ------------------------------------------------------------------
    $nAL = Exec-NonQuery $Conn $Txn @"
IF NOT EXISTS (SELECT 1 FROM ALBalance WHERE EmployeeId = @id)
INSERT INTO ALBalance
    (EmployeeId, EmployeeName, EligibleDays, PlannedTakenAL, RemainingAL,
     CountSL, CountUL, CountWorkingSundays, CountFreeSundays, LastUpdated)
VALUES
    (@id, @name, 28, 0, 28, 0, 0, 0, 0, GETDATE())
"@ @{ id=$EmpId; name=$FullName }

    if ($nAL -eq 1) {
        Write-Host "  ALBalance: inserted 1 row (EligibleDays=28)" -ForegroundColor Green
    } else {
        Write-Host "  ALBalance: already exists -- skipped" -ForegroundColor Yellow
    }

    $Txn.Commit()
    Write-Host ""
    Write-Host "COMMIT OK" -ForegroundColor Green

} catch {
    Write-Host ""
    Write-Host ("ERROR: {0}" -f $_) -ForegroundColor Red
    try { $Txn.Rollback() } catch {}
    Write-Host "ROLLBACK -- nothing was committed." -ForegroundColor Yellow
    $Conn.Close()
    exit 1
}

$Conn.Close()

# ------------------------------------------------------------------
# Verification (fresh connection, no transaction)
# ------------------------------------------------------------------
Write-Host ""
Write-Host "Verification ..." -ForegroundColor Cyan

$C2 = New-Conn

$vEmp  = Exec-Scalar $C2 $null "SELECT COUNT(*) FROM Employees    WHERE EmployeeId=@id" @{id=$EmpId}
$vBL   = Exec-Scalar $C2 $null "SELECT Bundesland                 FROM Employees WHERE EmployeeId=@id" @{id=$EmpId}
$vSE   = Exec-Scalar $C2 $null "SELECT COUNT(*) FROM ShiftEntries WHERE EmployeeId=@id" @{id=$EmpId}
$vWork = Exec-Scalar $C2 $null "SELECT COUNT(*) FROM ShiftEntries WHERE EmployeeId=@id AND ShiftType='WORKING'" @{id=$EmpId}
$vWE   = Exec-Scalar $C2 $null "SELECT COUNT(*) FROM ShiftEntries WHERE EmployeeId=@id AND ShiftType='OFF_WEEKEND'" @{id=$EmpId}
$vPH   = Exec-Scalar $C2 $null "SELECT COUNT(*) FROM ShiftEntries WHERE EmployeeId=@id AND ShiftType IN ('PH','LPH')" @{id=$EmpId}
$vAL   = Exec-Scalar $C2 $null "SELECT COUNT(*) FROM ALBalance    WHERE EmployeeId=@id" @{id=$EmpId}

$C2.Close()

Write-Host ("  Employees    : {0}  Bundesland={1}" -f $vEmp, $vBL) -ForegroundColor $(if ($vEmp -eq 1) {"Green"} else {"Red"})
Write-Host ("  ShiftEntries : {0} total  ({1} WORKING + {2} OFF_WEEKEND + {3} PH/LPH)" -f $vSE, $vWork, $vWE, $vPH) -ForegroundColor $(if ($vSE -gt 0) {"Green"} else {"Red"})
Write-Host ("  ALBalance    : {0}  (expect 1)" -f $vAL) -ForegroundColor $(if ($vAL -eq 1) {"Green"} else {"Red"})

Write-Host ""
if ($vEmp -eq 1 -and $vSE -gt 0 -and $vAL -eq 1) {
    Write-Host "=== DONE: Perim Rollin je u sistemu. ===" -ForegroundColor Green
    Write-Host "  Next: pwsh -File C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1" -ForegroundColor Yellow
} else {
    Write-Host "=== VERIFICATION FAILED ===" -ForegroundColor Red
    exit 1
}
Write-Host ""
