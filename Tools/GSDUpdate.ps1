<#
.SYNOPSIS
    GSD Dashboard — brzi update agenta u bazi.

.EXAMPLES
    # Update shift vremena
    .\GSDUpdate.ps1 -Action shift -Name "Elaheh Ramzi" -Start "08:00" -End "17:00"

    # Sick leave (bez end date = ACTIVE)
    .\GSDUpdate.ps1 -Action sick -Name "Sharon Huber" -StartDate "2026-07-14"

    # Sick leave sa end datumom (CLOSED)
    .\GSDUpdate.ps1 -Action sick -Name "Sharon Huber" -StartDate "2026-07-14" -EndDate "2026-07-18"

    # Zatvori sick leave (vrati se na posao danas)
    .\GSDUpdate.ps1 -Action sick-close -Name "Anisha Nellikka Panikkan"

    # Annual leave
    .\GSDUpdate.ps1 -Action al -Name "Danny Bendig" -StartDate "2026-08-10" -EndDate "2026-08-21" -Status "APPROVED"

    # Provjera agenta danas
    .\GSDUpdate.ps1 -Action status -Name "Elaheh Ramzi"
#>

param(
    [Parameter(Mandatory)][ValidateSet("shift","sick","sick-close","al","status")][string]$Action,
    [Parameter(Mandatory)][string]$Name,
    [string]$Start,
    [string]$End,
    [string]$StartDate = (Get-Date -Format "yyyy-MM-dd"),
    [string]$EndDate,
    [ValidateSet("APPROVED","PENDING_APPROVAL","REJECTED")][string]$Status = "PENDING_APPROVAL",
    [string]$Date = (Get-Date -Format "yyyy-MM-dd"),
    [string]$Server = "localhost\SQLEXPRESS",
    [string]$Database = "GSDDashboard"
)

$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True;"

function Exec-Query {
    param([string]$sql, [hashtable]$params = @{})
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    foreach ($key in $params.Keys) {
        $cmd.Parameters.AddWithValue("@$key", $params[$key]) | Out-Null
    }
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
    $ds = New-Object System.Data.DataSet
    $adapter.Fill($ds) | Out-Null
    return $ds.Tables[0]
}

function Exec-NonQuery {
    param([string]$sql, [hashtable]$params = @{})
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    foreach ($key in $params.Keys) {
        $cmd.Parameters.AddWithValue("@$key", $params[$key]) | Out-Null
    }
    return $cmd.ExecuteNonQuery()
}

function Get-EmployeeId {
    param([string]$fullName)
    $result = Exec-Query "SELECT EmployeeId, FullName FROM Employees WHERE FullName = @name AND IsActive = 1" @{ name = $fullName }
    if ($result.Rows.Count -eq 0) {
        # Pokušaj partial match
        $result = Exec-Query "SELECT EmployeeId, FullName FROM Employees WHERE FullName LIKE @name AND IsActive = 1" @{ name = "%$fullName%" }
    }
    if ($result.Rows.Count -eq 0) {
        Write-Host "GRESKA: Agent '$fullName' nije pronadjen u bazi." -ForegroundColor Red
        return $null
    }
    if ($result.Rows.Count -gt 1) {
        Write-Host "Vise rezultata za '$fullName':" -ForegroundColor Yellow
        $result | Format-Table EmployeeId, FullName | Out-Host
        Write-Host "Precizni naziv agenta." -ForegroundColor Yellow
        return $null
    }
    return $result.Rows[0]["EmployeeId"]
}

try {
    $conn.Open()
    Write-Host "Konekcija OK -> $Database" -ForegroundColor Green

    $eid = Get-EmployeeId $Name
    if (-not $eid) { exit 1 }
    Write-Host "Agent: $Name | EmpId: $eid" -ForegroundColor Cyan

    switch ($Action) {

        "shift" {
            if (-not $Start -or -not $End) {
                Write-Host "GRESKA: -Start i -End su obavezni za 'shift' akciju." -ForegroundColor Red
                exit 1
            }
            $rows = Exec-NonQuery @"
                UPDATE ShiftEntries
                SET ShiftStart = @start, ShiftEnd = @end, SourceModule = 'MANUAL'
                WHERE EmployeeId = @eid AND ShiftDate = @date
"@ @{ eid = $eid; start = $Start; end = $End; date = $Date }

            if ($rows -eq 0) {
                Write-Host "Nema postojeceg shifta -> unosim novi red." -ForegroundColor Yellow
                Exec-NonQuery @"
                    INSERT INTO ShiftEntries (EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, SourceSheet, SourceModule)
                    VALUES (@eid, @date, 'GSD', @start, @end, 'MANUAL', 'MANUAL')
"@ @{ eid = $eid; date = $Date; start = $Start; end = $End } | Out-Null
            }
            Write-Host "OK  Shift azuriran: $Start - $End za $Date" -ForegroundColor Green
        }

        "sick" {
            $endVal = if ($EndDate) { $EndDate } else { "2099-12-31" }
            $dur    = if ($EndDate) { ([datetime]$EndDate - [datetime]$StartDate).Days + 1 } else { $null }

            $existing = Exec-Query "SELECT Id FROM SickLeaves WHERE EmployeeId = @eid AND FirstDay = @sd" @{ eid = $eid; sd = $StartDate }
            if ($existing.Rows.Count -gt 0) {
                Exec-NonQuery @"
                    UPDATE SickLeaves
                    SET LastDay = @ed, DurationDays = @dur, SourceSheet = 'MANUAL'
                    WHERE EmployeeId = @eid AND FirstDay = @sd
"@ @{ eid = $eid; sd = $StartDate; ed = $endVal; dur = if ($dur) { $dur } else { [DBNull]::Value } } | Out-Null
                Write-Host "OK  Sick leave azuriran ($StartDate -> $endVal)" -ForegroundColor Green
            } else {
                $emp = Exec-Query "SELECT FirstName, LastName, TeamLeadName FROM Employees WHERE EmployeeId = @eid" @{ eid = $eid }
                Exec-NonQuery @"
                    INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, SourceSheet, CreatedAt)
                    VALUES (@eid, @fn, @ln, @tl, @sd, @ed, @dur, 'Self', 'MANUAL', GETUTCDATE())
"@ @{
                    eid = $eid
                    fn  = $emp.Rows[0]["FirstName"]
                    ln  = $emp.Rows[0]["LastName"]
                    tl  = $emp.Rows[0]["TeamLeadName"]
                    sd  = $StartDate
                    ed  = $endVal
                    dur = if ($dur) { $dur } else { [DBNull]::Value }
                } | Out-Null
                Write-Host "OK  Sick leave unesen ($StartDate -> $endVal)" -ForegroundColor Green
            }
        }

        "sick-close" {
            $today = Get-Date -Format "yyyy-MM-dd"
            $rows = Exec-NonQuery @"
                UPDATE SickLeaves
                SET LastDay = @today, DurationDays = DATEDIFF(day, FirstDay, @today) + 1
                WHERE EmployeeId = @eid AND LastDay = '2099-12-31'
"@ @{ eid = $eid; today = $today }
            if ($rows -eq 0) {
                Write-Host "Nije nadjen aktivan sick leave za $Name." -ForegroundColor Yellow
            } else {
                Write-Host "OK  Sick leave zatvoren (end date = $today)" -ForegroundColor Green
            }
        }

        "al" {
            $dur = ([datetime]$EndDate - [datetime]$StartDate).Days + 1
            $existing = Exec-Query "SELECT Id FROM Vacations WHERE EmployeeId = @eid AND FirstDay = @sd" @{ eid = $eid; sd = $StartDate }
            if ($existing.Rows.Count -gt 0) {
                Exec-NonQuery @"
                    UPDATE Vacations SET LastDay = @ed, WorkDaysNet = @dur, ApprovedDenied = @status, SourceSheet = 'MANUAL'
                    WHERE EmployeeId = @eid AND FirstDay = @sd
"@ @{ eid = $eid; sd = $StartDate; ed = $EndDate; dur = $dur; status = $Status } | Out-Null
                Write-Host "OK  Annual leave azuriran ($StartDate -> $EndDate | $Status)" -ForegroundColor Green
            } else {
                $emp = Exec-Query "SELECT FirstName, LastName FROM Employees WHERE EmployeeId = @eid" @{ eid = $eid }
                Exec-NonQuery @"
                    INSERT INTO Vacations (EmployeeId, FirstName, LastDay, FirstDay, WorkDaysNet, ApprovedDenied, SourceSheet, CreatedAt)
                    VALUES (@eid, @fn, @ed, @sd, @dur, @status, 'MANUAL', GETUTCDATE())
"@ @{
                    eid    = $eid
                    fn     = $emp.Rows[0]["FirstName"]
                    sd     = $StartDate
                    ed     = $EndDate
                    dur    = $dur
                    status = $Status
                } | Out-Null
                Write-Host "OK  Annual leave unesen ($StartDate -> $EndDate | $Status)" -ForegroundColor Green
            }
        }

        "status" {
            Write-Host "`n--- Shift ($Date) ---" -ForegroundColor Yellow
            Exec-Query "SELECT ShiftDate, ShiftType, ShiftStart, ShiftEnd, AgentTask FROM ShiftEntries WHERE EmployeeId = @eid AND ShiftDate = @date" @{ eid = $eid; date = $Date } | Format-Table | Out-Host

            Write-Host "--- Aktivan Sick Leave ---" -ForegroundColor Yellow
            Exec-Query "SELECT FirstDay, LastDay, LeaveType, Comments FROM SickLeaves WHERE EmployeeId = @eid AND LastDay >= GETDATE()" @{ eid = $eid } | Format-Table | Out-Host

            Write-Host "--- Nadolazeci Annual Leave ---" -ForegroundColor Yellow
            Exec-Query "SELECT FirstDay, LastDay, WorkDaysNet, ApprovedDenied FROM Vacations WHERE EmployeeId = @eid AND LastDay >= GETDATE()" @{ eid = $eid } | Format-Table | Out-Host
        }
    }
}
catch {
    Write-Host "GRESKA: $_" -ForegroundColor Red
    exit 1
}
finally {
    $conn.Close()
}
