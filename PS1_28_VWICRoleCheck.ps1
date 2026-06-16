# PS1_28_VWICRoleCheck.ps1
# Check PrimaryRole / SecondaryRole values and VWIC agent status.

$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

function Run-Query($conn, [string]$title, [string]$sql) {
    Write-Host ""
    Write-Host "--- $title ---" -ForegroundColor Cyan
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $r = $cmd.ExecuteReader()
    $cols = @(0..($r.FieldCount - 1) | ForEach-Object { $r.GetName($_) })
    # header
    Write-Host ($cols -join "  |  ") -ForegroundColor Yellow
    Write-Host ("-" * 80) -ForegroundColor DarkGray
    while ($r.Read()) {
        $vals = $cols | ForEach-Object {
            if ($r.IsDBNull($r.GetOrdinal($_))) { "NULL" } else { $r[$_].ToString() }
        }
        Write-Host ($vals -join "  |  ")
    }
    $r.Close()
}

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
try {
    $conn.Open()
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Yellow
    Write-Host "  PS1_28: VWIC Role Check" -ForegroundColor Yellow
    Write-Host "============================================" -ForegroundColor Yellow

    Run-Query $conn "1. Distinct PrimaryRole values" @"
SELECT DISTINCT PrimaryRole, COUNT(*) AS EmployeeCount
FROM Employees
GROUP BY PrimaryRole
ORDER BY PrimaryRole
"@

    Run-Query $conn "2. Distinct SecondaryRole values" @"
SELECT DISTINCT SecondaryRole, COUNT(*) AS EmployeeCount
FROM Employees
GROUP BY SecondaryRole
ORDER BY SecondaryRole
"@

    Run-Query $conn "3. VWIC-related agents by EmployeeId" @"
SELECT
    EmployeeId,
    FullName,
    ISNULL(PrimaryRole,  'NULL') AS PrimaryRole,
    ISNULL(SecondaryRole,'NULL') AS SecondaryRole,
    ISNULL(TeamLeadName, 'NULL') AS TeamLead,
    CAST(IsActive AS INT)        AS IsActive
FROM Employees
WHERE EmployeeId IN (
    '9074375','9075030','9074373',
    '9074341','4451025','4451022','9078602','9074535',
    '9085123','9074592','9126877','9120970','3193180',
    '3193174','3193175','3193178','3193177','9074381',
    '9133999'
)
ORDER BY FullName
"@

    # Count how many were found vs expected
    $cmd2 = $conn.CreateCommand()
    $cmd2.CommandText = @"
SELECT COUNT(*) FROM Employees
WHERE EmployeeId IN (
    '9074375','9075030','9074373',
    '9074341','4451025','4451022','9078602','9074535',
    '9085123','9074592','9126877','9120970','3193180',
    '3193174','3193175','3193178','3193177','9074381',
    '9133999'
)
"@
    $found = $cmd2.ExecuteScalar()
    Write-Host ""
    Write-Host "Found $found of 19 expected employee IDs in DB." -ForegroundColor $(if ($found -eq 19) { "Green" } else { "Red" })

    # Show which IDs are MISSING
    if ($found -lt 19) {
        Run-Query $conn "4. Missing IDs (not in Employees table)" @"
SELECT v.EmployeeId
FROM (VALUES
    ('9074375'),('9075030'),('9074373'),
    ('9074341'),('4451025'),('4451022'),('9078602'),('9074535'),
    ('9085123'),('9074592'),('9126877'),('9120970'),('3193180'),
    ('3193174'),('3193175'),('3193178'),('3193177'),('9074381'),
    ('9133999')
) AS v(EmployeeId)
LEFT JOIN Employees e ON e.EmployeeId = v.EmployeeId
WHERE e.EmployeeId IS NULL
"@
    }

    # Also show any employees whose name fuzzy-matches VWIC agents (catch ID mismatches)
    Run-Query $conn "5. DB rows with SecondaryRole = 'VWIC' or PrimaryRole = 'VWIC'" @"
SELECT EmployeeId, FullName,
    ISNULL(PrimaryRole,'NULL')   AS PrimaryRole,
    ISNULL(SecondaryRole,'NULL') AS SecondaryRole,
    ISNULL(TeamLeadName,'NULL')  AS TeamLead,
    CAST(IsActive AS INT)        AS IsActive
FROM Employees
WHERE PrimaryRole = 'VWIC' OR SecondaryRole = 'VWIC'
ORDER BY FullName
"@

} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
} finally {
    $conn.Close()
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "  PS1_28 complete" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
