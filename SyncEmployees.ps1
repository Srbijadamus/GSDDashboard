#Requires -Version 7.0
<#
.SYNOPSIS
    Sinkronizacija zaposlenih u GSDDashboard bazi sa Azure AD listom.
#>

$connectionString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"

function Invoke-SQL {
    param([string]$Query, [string]$Label = "")
    $conn = [System.Data.SqlClient.SqlConnection]::new($connectionString)
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Query

        if ($Query.TrimStart().ToUpper().StartsWith("SELECT")) {
            $adapter = [System.Data.SqlClient.SqlDataAdapter]::new($cmd)
            $table = [System.Data.DataTable]::new()
            $adapter.Fill($table) | Out-Null
            return $table
        } else {
            $rows = $cmd.ExecuteNonQuery()
            if ($Label) { Write-Host "  ✓ $Label ($rows red(ova) affected)" -ForegroundColor Green }
            return $rows
        }
    } finally {
        $conn.Close()
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "   GSDDashboard — Employee Sync" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ─────────────────────────────────────────────────────
# 1. DEAKTIVIRAJ: Kavinraj Pathmanathan (nema Azure nalog)
# ─────────────────────────────────────────────────────
Write-Host "[ 1 ] Deaktiviraj Kavinraj Pathmanathan..." -ForegroundColor Yellow
$check = Invoke-SQL "SELECT EmployeeId, FullName, IsActive FROM Employees WHERE EonEmail = 'Kavin.Pathmanathan.external@eon.com'"
if ($check.Rows.Count -eq 0) {
    Write-Host "  ! Nije pronađen u DB — preskačem." -ForegroundColor DarkYellow
} elseif ($check.Rows[0]["IsActive"] -eq $false) {
    Write-Host "  ! Već je neaktivan — preskačem." -ForegroundColor DarkYellow
} else {
    Invoke-SQL "UPDATE Employees SET IsActive = 0 WHERE EonEmail = 'Kavin.Pathmanathan.external@eon.com'" -Label "Kavinraj deaktiviran"
}

# ─────────────────────────────────────────────────────
# 2. PROVJERI/UKLONI: Mohamed Khaled Mahmoud
# ─────────────────────────────────────────────────────
Write-Host ""
Write-Host "[ 2 ] Provjeri Mohamed Khaled Mahmoud..." -ForegroundColor Yellow
$mkm = Invoke-SQL "SELECT EmployeeId, FullName, IsActive FROM Employees WHERE EonEmail = 'Mohamed.Khaled.Mahmoud.external@eon.com' OR FullName LIKE '%Mahmoud%'"
if ($mkm.Rows.Count -eq 0) {
    Write-Host "  ✓ Nije u DB — nema šta raditi." -ForegroundColor Green
} else {
    foreach ($row in $mkm.Rows) {
        Write-Host "  Pronađen: $($row['FullName']) | IsActive: $($row['IsActive'])" -ForegroundColor DarkYellow
    }
    Invoke-SQL "UPDATE Employees SET IsActive = 0 WHERE EonEmail = 'Mohamed.Khaled.Mahmoud.external@eon.com'" -Label "Mohamed Khaled Mahmoud deaktiviran"
}

# ─────────────────────────────────────────────────────
# 3. DODAJ: Elias Erdem
# ─────────────────────────────────────────────────────
Write-Host ""
Write-Host "[ 3 ] Dodaj Elias Erdem..." -ForegroundColor Yellow
$ee = Invoke-SQL "SELECT EmployeeId FROM Employees WHERE EonEmail = 'Elias.Erdem.external@eon.com' OR EmployeeId = 'E26615'"
if ($ee.Rows.Count -gt 0) {
    Write-Host "  ! Već postoji u DB — preskačem." -ForegroundColor DarkYellow
} else {
    Invoke-SQL @"
INSERT INTO Employees (EmployeeId, FirstName, LastName, FullName, EonEmail, IsActive, IsTrainee, CreatedAt, SourceSheet)
VALUES ('E26615', 'Elias', 'Erdem', 'Elias Erdem', 'Elias.Erdem.external@eon.com', 1, 0, GETUTCDATE(), 'GSD_DE')
"@ -Label "Elias Erdem dodan"
}

# ─────────────────────────────────────────────────────
# 4. DODAJ: Patrick Henschel
# ─────────────────────────────────────────────────────
Write-Host ""
Write-Host "[ 4 ] Dodaj Patrick Henschel..." -ForegroundColor Yellow
$ph = Invoke-SQL "SELECT EmployeeId FROM Employees WHERE EonEmail = 'Patrick.Henschel.external@eon.com' OR EmployeeId = 'P37233'"
if ($ph.Rows.Count -gt 0) {
    Write-Host "  ! Već postoji u DB — preskačem." -ForegroundColor DarkYellow
} else {
    Invoke-SQL @"
INSERT INTO Employees (EmployeeId, FirstName, LastName, FullName, EonEmail, IsActive, IsTrainee, CreatedAt, SourceSheet)
VALUES ('P37233', 'Patrick', 'Henschel', 'Patrick Henschel', 'Patrick.Henschel.external@eon.com', 1, 0, GETUTCDATE(), 'GSD_DE')
"@ -Label "Patrick Henschel dodan"
}

# ─────────────────────────────────────────────────────
# 5. FINALNI PREGLED
# ─────────────────────────────────────────────────────
Write-Host ""
Write-Host "[ 5 ] Finalni pregled aktivnih zaposlenih..." -ForegroundColor Yellow
$all = Invoke-SQL "SELECT FullName, EonEmail FROM Employees WHERE IsActive = 1 AND EonEmail IS NOT NULL ORDER BY FullName"
Write-Host ""
Write-Host "  Ukupno aktivnih sa EON emailom: $($all.Rows.Count)" -ForegroundColor Cyan
Write-Host ""
$all | Format-Table -AutoSize

Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "   Gotovo!" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
