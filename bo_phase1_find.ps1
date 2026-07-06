# bo_phase1_find.ps1
# PHASE 1: Read-only name resolution for the 2026-07-06 BO list.
# Matches each name to Employees.FullName (exact CI_AI, then last-name fallback).
# Nothing is inserted or changed.
#
# Run with: pwsh -File C:\GSDDashboard\bo_phase1_find.ps1

$ConnString = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Integrated Security=true;TrustServerCertificate=yes;"

Add-Type -AssemblyName "System.Data"

# BO list: Name, Start, End, Note, MaybeAbsent
# MaybeAbsent = $true means the name is expected to possibly not be in the DB
$BoList = @(
    [PSCustomObject]@{ Name="Sina Sidharthan";          Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Baschir Mahrufi";          Start="07:00"; End="16:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Javier Sang";              Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$true  },
    [PSCustomObject]@{ Name="Stefan Becker";            Start="07:00"; End="16:00"; Note="";        MaybeAbsent=$true  },
    [PSCustomObject]@{ Name="Kai Eric Kumlehn";         Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Mohamad Nasir Amany";      Start="08:00"; End="17:00"; Note="LEW";     MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Lukas Schiefele";          Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Anifa Ngcongo";            Start="10:00"; End="17:00"; Note="";        MaybeAbsent=$true  },
    [PSCustomObject]@{ Name="Tim Nguyen";               Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Hamyaz Pathan";            Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Sebastian Lewandowski";    Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Khaled Alali";             Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Elaheh Ramzi";             Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Viktor Winter";            Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Marko Bosnjak";            Start="08:00"; End="17:00"; Note="Newjoiner"; MaybeAbsent=$true },
    [PSCustomObject]@{ Name="Krishnendu Das";           Start="09:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Duc Quy Huynh";            Start="07:00"; End="16:00"; Note="Enviam";  MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Amani Kedo";               Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Ion Bodnariuc";            Start="08:00"; End="17:00"; Note="Enviam";  MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Yun Hee Oh";               Start="08:00"; End="13:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Ahmad Dabbas";             Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Suhrab Sadieqy";           Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Anisha Nellikka Panikkan"; Start="08:00"; End="17:00"; Note="";        MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Dmytro Shelikhov";         Start="08:00"; End="17:00"; Note="BAG WIC"; MaybeAbsent=$false },
    [PSCustomObject]@{ Name="Felix Spindler";           Start="08:00"; End="17:00"; Note="Enviam";  MaybeAbsent=$false }
)

# Extra search alias for Amani Kedo (DB may store as "Aman Kedo")
$Aliases = @{
    "Amani Kedo" = @("Amani Kedo", "Aman Kedo")
}

$Conn = New-Object System.Data.SqlClient.SqlConnection($ConnString)
$Conn.Open()

function Invoke-Rows([string]$Sql, [hashtable]$Params = @{}) {
    $cmd = $script:Conn.CreateCommand()
    $cmd.CommandText = $Sql
    foreach ($kv in $Params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    $reader = $cmd.ExecuteReader()
    $rows = @()
    while ($reader.Read()) {
        $row = @{}
        for ($i = 0; $i -lt $reader.FieldCount; $i++) {
            $raw = $reader.GetValue($i)
            $row[$reader.GetName($i)] = if ($raw -is [System.DBNull]) { $null } else { $raw }
        }
        $rows += [PSCustomObject]$row
    }
    $reader.Close()
    return $rows
}

Write-Host ""
Write-Host "=== BO PHASE 1: NAME RESOLUTION for 2026-07-06 ===" -ForegroundColor Yellow
Write-Host "Nothing will be inserted."                           -ForegroundColor Yellow
Write-Host ""
Write-Host "--- Matching {0} names against Employees.FullName ---" -f $BoList.Count -ForegroundColor Cyan
Write-Host ""

$Matched   = @()
$Unmatched = @()
$Ambiguous = @()

foreach ($entry in $BoList) {
    $inputName = $entry.Name

    # Build the list of search terms for this name
    $searchTerms = @()
    if ($Aliases.ContainsKey($inputName)) {
        $searchTerms = @($Aliases[$inputName])
    } else {
        $searchTerms = @($inputName)
    }

    # Collect unique hits across all search terms
    $hits = @{}
    foreach ($term in $searchTerms) {
        $rows = @(Invoke-Rows `
            "SELECT EmployeeId, FullName, PrimaryRole, IsActive FROM Employees WHERE FullName = @n COLLATE Latin1_General_CI_AI" `
            @{n=$term})
        foreach ($r in $rows) {
            $key = "$($r.EmployeeId)"
            if (-not $hits.ContainsKey($key)) { $hits[$key] = $r }
        }
    }

    # Fallback: search by last word of name if exact found nothing
    if ($hits.Count -eq 0) {
        $lastName  = $inputName.Trim().Split(" ")[-1]
        $rows = @(Invoke-Rows `
            "SELECT EmployeeId, FullName, PrimaryRole, IsActive FROM Employees WHERE FullName LIKE @pat COLLATE Latin1_General_CI_AI" `
            @{pat="%$lastName%"})
        foreach ($r in $rows) {
            $key = "$($r.EmployeeId)"
            if (-not $hits.ContainsKey($key)) { $hits[$key] = $r }
        }
    }

    $unique = @()
    foreach ($k in @($hits.Keys)) { $unique += $hits[$k] }

    $maybeTag = if ($entry.MaybeAbsent) { " [possibly not in DB]" } else { "" }

    if ($unique.Count -eq 0) {
        $color = if ($entry.MaybeAbsent) { "DarkYellow" } else { "Red" }
        Write-Host ("  NOT FOUND:  '{0}'{1}" -f $inputName, $maybeTag) -ForegroundColor $color
        $Unmatched += [PSCustomObject]@{
            InputName    = $inputName
            Start        = $entry.Start
            End          = $entry.End
            Note         = $entry.Note
            MaybeAbsent  = $entry.MaybeAbsent
        }
    } elseif ($unique.Count -eq 1) {
        $emp = $unique[0]
        $nameMatch = if ("$($emp.FullName)" -eq $inputName) { "" } else { "  [DB name differs]" }
        Write-Host ("  OK:         '{0}'  ->  ID={1}  DB='{2}'  Role={3}  Active={4}{5}" -f `
            $inputName, $emp.EmployeeId, $emp.FullName, $emp.PrimaryRole, $emp.IsActive, $nameMatch) -ForegroundColor Green
        $Matched += [PSCustomObject]@{
            InputName    = $inputName
            EmployeeId   = "$($emp.EmployeeId)"
            FullName     = "$($emp.FullName)"
            PrimaryRole  = "$($emp.PrimaryRole)"
            IsActive     = "$($emp.IsActive)"
            Start        = $entry.Start
            End          = $entry.End
            Note         = $entry.Note
        }
    } else {
        Write-Host ("  AMBIGUOUS ({0}): '{1}'" -f $unique.Count, $inputName) -ForegroundColor Yellow
        foreach ($emp in $unique) {
            Write-Host ("    ID={0}  FullName='{1}'  Role={2}  Active={3}" -f `
                $emp.EmployeeId, $emp.FullName, $emp.PrimaryRole, $emp.IsActive) -ForegroundColor Yellow
        }
        $Ambiguous += $inputName
        $Unmatched += [PSCustomObject]@{
            InputName    = $inputName
            Start        = $entry.Start
            End          = $entry.End
            Note         = $entry.Note
            MaybeAbsent  = $false
        }
    }
}

$Conn.Close()

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host ("--- Summary ---") -ForegroundColor Cyan
Write-Host ""
Write-Host ("  Matched:    {0}" -f $Matched.Count)   -ForegroundColor Green
Write-Host ("  Unmatched:  {0}" -f $Unmatched.Count) -ForegroundColor $(if ($Unmatched.Count -gt 0) { "Red" } else { "Green" })
Write-Host ("  Ambiguous:  {0}" -f $Ambiguous.Count) -ForegroundColor $(if ($Ambiguous.Count -gt 0) { "Yellow" } else { "Green" })
Write-Host ""

if ($Unmatched.Count -gt 0) {
    Write-Host "  Unmatched / Not Found:" -ForegroundColor Red
    foreach ($u in $Unmatched) {
        $tag = if ($u.MaybeAbsent) { "  [expected - possibly new/not in DB]" } else { "  [UNEXPECTED - investigate]" }
        Write-Host ("    '{0}' {1}-{2}{3}" -f $u.InputName, $u.Start, $u.End, $tag) -ForegroundColor DarkYellow
    }
    Write-Host ""
}

Write-Host "  Matched employees (would be inserted as BO ShiftEntries for 2026-07-06):" -ForegroundColor Cyan
Write-Host ("  {0,-14} {1,-34} {2,-6} {3,-6} {4}" -f "EmployeeId", "FullName", "Start", "End", "Note")
Write-Host ("  {0,-14} {1,-34} {2,-6} {3,-6} {4}" -f ("-"*14), ("-"*34), ("-"*6), ("-"*6), ("-"*10))
foreach ($m in $Matched) {
    Write-Host ("  {0,-14} {1,-34} {2,-6} {3,-6} {4}" -f $m.EmployeeId, $m.FullName, $m.Start, $m.End, $m.Note) -ForegroundColor White
}

Write-Host ""
Write-Host "=== PHASE 1 COMPLETE -- READ-ONLY, NOTHING INSERTED ===" -ForegroundColor Yellow
Write-Host "Confirm ShiftType choice + employee matches, then proceed to Phase 2."  -ForegroundColor White
Write-Host ""
