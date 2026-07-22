$connStr = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()

# Pull all active employees with role
$sql = @"
SELECT EmployeeId, FullName, PrimaryRole, TeamLeadName
FROM Employees
WHERE IsActive = 1
ORDER BY PrimaryRole, FullName
"@
$cmd = New-Object System.Data.SqlClient.SqlCommand($sql, $conn)
$r = $cmd.ExecuteReader()

$wic   = @()
$vwic  = @()
$voice = @()
$other = @()

while ($r.Read()) {
    $obj = [PSCustomObject]@{
        Id       = [string]$r["EmployeeId"]
        Name     = [string]$r["FullName"]
        Role     = [string]$r["PrimaryRole"]
        TL       = [string]$r["TeamLeadName"]
    }
    switch ($obj.Role) {
        "WIC"   { $wic   += $obj }
        "VWIC"  { $vwic  += $obj }
        "Voice" { $voice += $obj }
        default { $other += $obj }
    }
}
$r.Close()
$conn.Close()

$total = $wic.Count + $vwic.Count + $voice.Count + $other.Count

# ── Console output ────────────────────────────────────────────────────────────

Write-Host ""
Write-Host ("=== Active Agents: {0} total ===" -f $total) -ForegroundColor Yellow
Write-Host ""

Write-Host ("WIC ({0}):" -f $wic.Count) -ForegroundColor Cyan
$wic | ForEach-Object { Write-Host ("  {0,-10}  {1}" -f $_.Id, $_.Name) }

Write-Host ""
Write-Host ("VWIC ({0}):" -f $vwic.Count) -ForegroundColor Cyan
$vwic | ForEach-Object { Write-Host ("  {0,-10}  {1}" -f $_.Id, $_.Name) }

Write-Host ""
Write-Host ("Voice ({0}):" -f $voice.Count) -ForegroundColor Cyan
$voice | ForEach-Object { Write-Host ("  {0,-10}  {1}" -f $_.Id, $_.Name) }

Write-Host ""
Write-Host ("Other ({0}):" -f $other.Count) -ForegroundColor Cyan
$other | ForEach-Object { Write-Host ("  {0,-10}  {1,-35}  {2}" -f $_.Id, $_.Name, $_.Role) }

# ── Build AI prompt ───────────────────────────────────────────────────────────

$wicNames   = ($wic   | ForEach-Object { $_.Name }) -join ", "
$vwicNames  = ($vwic  | ForEach-Object { $_.Name }) -join ", "
$voiceNames = ($voice | ForEach-Object { $_.Name }) -join ", "
$otherLines = ($other | ForEach-Object { "{0} ({1})" -f $_.Name, $_.Role }) -join ", "

$prompt = @"
You are an AI assistant for the E.ON Global Service Desk (GSD) operations team.

Below is the current list of ACTIVE GSD agents as of today ($((Get-Date).ToString('yyyy-MM-dd'))), organized by role.

---

WIC AGENTS ($($wic.Count)):
$($wic | ForEach-Object { "- " + $_.Name } | Out-String)
VWIC AGENTS ($($vwic.Count)):
$($vwic | ForEach-Object { "- " + $_.Name } | Out-String)
VOICE AGENTS ($($voice.Count)):
$($voice | ForEach-Object { "- " + $_.Name } | Out-String)
OTHER AGENTS ($($other.Count)):
$($other | ForEach-Object { "- {0} [{1}]" -f $_.Name, $_.Role } | Out-String)
---

TOTAL ACTIVE: $total agents
  WIC:   $($wic.Count)
  VWIC:  $($vwic.Count)
  Voice: $($voice.Count)
  Other: $($other.Count)

---

When asked about agent availability, absence, assignments, or coverage — use the above list as your ground truth for who is currently active on the team.
"@

# Save prompt to file
$promptPath = "C:\GSDDashboard\ai_prompt_agents_$(Get-Date -Format 'yyyyMMdd').txt"
$prompt | Out-File -FilePath $promptPath -Encoding UTF8

Write-Host ""
Write-Host ("=== AI prompt saved to: {0} ===" -f $promptPath) -ForegroundColor Green
Write-Host ""
Write-Host "---- AI PROMPT PREVIEW ----" -ForegroundColor Magenta
Write-Host $prompt
Write-Host "---------------------------" -ForegroundColor Magenta
