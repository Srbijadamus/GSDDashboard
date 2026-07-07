# PS1_68_PerimRollinNRW.ps1
# Two fixes for Perim Rollin (EmployeeId=9074559, lives in Duesseldorf/NRW):
#   1. Set Employee.Bundesland = 'Nordrhein-Westfalen'
#   2. Fix Dec 25 shift: WORKING -> PH (national public holiday, falls on Friday)
#
# NRW holidays Jul 7..Dec 31 2026 that fall on a weekday:
#   Dec 25 (Fri) - 1. Weihnachtstag - ONLY ONE that needs correction.
#   Oct 3 (Sat), Nov 1 (Sun), Dec 26 (Sat) are already OFF_WEEKEND.
#
# Run with: pwsh -File C:\GSDDashboard\PS1_68_PerimRollinNRW.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"
$EmpId      = "9074559"
$FullName   = "Perim Rollin"
$Bundesland = "Nordrhein-Westfalen"

# National holiday on a weekday within her shift range
$NationalPH = @("2026-12-25")   # 1. Weihnachtstag (Friday)

# NRW-only holidays on weekdays within her shift range
$RegionalPH = @()               # Allerheiligen Nov 1 = Sunday, no correction needed

Add-Type -AssemblyName "System.Data"

function Open-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection($ConnString)
    $c.Open()
    return $c
}

Write-Host ""
Write-Host "=== PS1_68: Perim Rollin NRW / Bundesland fix ===" -ForegroundColor Cyan
Write-Host "  EmployeeId : $EmpId" -ForegroundColor White
Write-Host "  Bundesland : $Bundesland" -ForegroundColor White
Write-Host "  PH dates   : $($NationalPH -join ', ')  (national)" -ForegroundColor White
Write-Host "  LPH dates  : (none on weekdays in this period)" -ForegroundColor DarkGray
Write-Host ""

$Conn = Open-Conn
$xact = $Conn.CreateCommand(); $xact.CommandText = "SET XACT_ABORT ON"; [void]$xact.ExecuteNonQuery()
$Txn  = $Conn.BeginTransaction()

try {

    # ------------------------------------------------------------------
    # 1. Set Employee.Bundesland
    # ------------------------------------------------------------------
    $cmd = $Conn.CreateCommand(); $cmd.Transaction = $Txn
    $cmd.CommandText = "UPDATE Employees SET Bundesland = @bl WHERE EmployeeId = @id"
    [void]$cmd.Parameters.AddWithValue("@bl", $Bundesland)
    [void]$cmd.Parameters.AddWithValue("@id", $EmpId)
    $n = $cmd.ExecuteNonQuery()
    Write-Host ("  Employees.Bundesland updated: {0} row(s)" -f $n) -ForegroundColor $(if ($n -eq 1) {"Green"} else {"Red"})

    # ------------------------------------------------------------------
    # 2. Fix national PH shifts
    # ------------------------------------------------------------------
    foreach ($d in $NationalPH) {
        $cmd2 = $Conn.CreateCommand(); $cmd2.Transaction = $Txn
        $cmd2.CommandText = @"
UPDATE ShiftEntries
SET ShiftType='PH', ShiftStart=NULL, ShiftEnd=NULL, RawValue='PH'
WHERE EmployeeId=@id AND ShiftDate=@d AND ShiftType='WORKING'
"@
        [void]$cmd2.Parameters.AddWithValue("@id", $EmpId)
        [void]$cmd2.Parameters.AddWithValue("@d",  $d)
        $n2 = $cmd2.ExecuteNonQuery()
        Write-Host ("  ShiftEntries $d  WORKING -> PH  ({0} row)" -f $n2) -ForegroundColor $(if ($n2 -eq 1) {"Green"} else {"Yellow"})
    }

    # ------------------------------------------------------------------
    # 3. Fix regional (NRW-only) PH shifts  -- none needed this period
    # ------------------------------------------------------------------
    foreach ($d in $RegionalPH) {
        $cmd3 = $Conn.CreateCommand(); $cmd3.Transaction = $Txn
        $cmd3.CommandText = @"
UPDATE ShiftEntries
SET ShiftType='LPH', ShiftStart=NULL, ShiftEnd=NULL, RawValue='LPH'
WHERE EmployeeId=@id AND ShiftDate=@d AND ShiftType='WORKING'
"@
        [void]$cmd3.Parameters.AddWithValue("@id", $EmpId)
        [void]$cmd3.Parameters.AddWithValue("@d",  $d)
        $n3 = $cmd3.ExecuteNonQuery()
        Write-Host ("  ShiftEntries $d  WORKING -> LPH  ({0} row)" -f $n3) -ForegroundColor $(if ($n3 -eq 1) {"Green"} else {"Yellow"})
    }

    $Txn.Commit()
    Write-Host ""
    Write-Host "COMMIT OK" -ForegroundColor Green

} catch {
    Write-Host ""
    Write-Host ("ERROR: {0}" -f $_) -ForegroundColor Red
    try { $Txn.Rollback() } catch {}
    Write-Host "ROLLBACK -- nothing committed." -ForegroundColor Yellow
    $Conn.Close()
    exit 1
}

$Conn.Close()

# ------------------------------------------------------------------
# Verification
# ------------------------------------------------------------------
Write-Host ""
Write-Host "Verification ..." -ForegroundColor Cyan

$Conn2 = Open-Conn

$cmd4 = $Conn2.CreateCommand()
$cmd4.CommandText = "SELECT Bundesland FROM Employees WHERE EmployeeId=@id"
[void]$cmd4.Parameters.AddWithValue("@id", $EmpId)
$bl = $cmd4.ExecuteScalar()
Write-Host ("  Bundesland  : {0}" -f $bl) -ForegroundColor $(if ($bl -eq $Bundesland) {"Green"} else {"Red"})

foreach ($d in $NationalPH) {
    $cmd5 = $Conn2.CreateCommand()
    $cmd5.CommandText = "SELECT ShiftType FROM ShiftEntries WHERE EmployeeId=@id AND ShiftDate=@d"
    [void]$cmd5.Parameters.AddWithValue("@id", $EmpId)
    [void]$cmd5.Parameters.AddWithValue("@d",  $d)
    $st = $cmd5.ExecuteScalar()
    Write-Host ("  ShiftEntries $d  ShiftType={0}" -f $st) -ForegroundColor $(if ($st -eq "PH") {"Green"} else {"Red"})
}

$Conn2.Close()
Write-Host ""
Write-Host "=== DONE ===" -ForegroundColor Green
Write-Host ""
