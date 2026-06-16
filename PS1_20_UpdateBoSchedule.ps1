# PS1_20_UpdateBoSchedule.ps1
# Updates today's shift records for the Bochum team based on the daily Bo Liste.
# Requires shifts to already exist in DB for today (imported from Excel previously).
# Usage: powershell -ExecutionPolicy Bypass -File PS1_20_UpdateBoSchedule.ps1

$ErrorActionPreference = "Stop"
$base  = "http://localhost:5000"
$today = (Get-Date).ToString("yyyy-MM-dd")

Write-Host "=== Bo Liste Schedule Update - $today ===" -ForegroundColor Cyan

# Schedule from today's Bo Liste.
# Third column = AgentTask. "BAG WIC" also sets ShiftType to WIC_DUTY.
$schedule = @(
    [PSCustomObject]@{ Name="Suhrab Sadieqy";    Start="08:00"; End="17:00"; Task=$null     }
    [PSCustomObject]@{ Name="Viktor Winter";     Start="08:00"; End="17:00"; Task=$null     }
    [PSCustomObject]@{ Name="Tunde Szabo";       Start="08:00"; End="17:00"; Task="2LV"     }
    [PSCustomObject]@{ Name="Hamza Forrousso";   Start="08:00"; End="16:30"; Task=$null     }
    [PSCustomObject]@{ Name="Duc Quy Huynh";     Start="07:00"; End="16:00"; Task="Enviam"  }
)

# Fetch today's shifts
Write-Host ""
Write-Host "Fetching shifts for $today ..." -ForegroundColor Gray
try {
    $raw = Invoke-WebRequest -Uri "$base/api/shifts?from=$today&to=$today" -UseBasicParsing
    $response = $raw.Content | ConvertFrom-Json
    $shifts = $response.value
}
catch {
    Write-Host "FATAL: Cannot reach $base/api/shifts - is the server running?" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

Write-Host "Found $($shifts.Count) shift record(s) in DB for today." -ForegroundColor Gray
Write-Host ""

# Process each entry
$ok       = 0
$notFound = 0
$errors   = 0

foreach ($entry in $schedule) {
    $shift = $shifts | Where-Object { $_.fullName -ieq $entry.Name } | Select-Object -First 1

    if (-not $shift) {
        Write-Host "  NOT FOUND  $($entry.Name)" -ForegroundColor Yellow
        $notFound++
        continue
    }

    if ($entry.Task -eq "BAG WIC") {
        $shiftType = "WIC_DUTY"
    }
    else {
        $shiftType = "WORKING"
    }

    $body = [ordered]@{
        shiftType  = $shiftType
        shiftStart = $entry.Start
        shiftEnd   = $entry.End
    }
    if ($null -ne $entry.Task) {
        $body.agentTask = $entry.Task
    }

    $bodyJson = $body | ConvertTo-Json

    try {
        Invoke-RestMethod -Uri "$base/api/shifts/$($shift.id)" -Method Patch -ContentType "application/json" -Body $bodyJson | Out-Null

        if ($null -ne $entry.Task) {
            $taskLabel = "  [$($entry.Task)]"
        }
        else {
            $taskLabel = ""
        }
        Write-Host "  OK  $($entry.Name.PadRight(35)) $($entry.Start)-$($entry.End)$taskLabel" -ForegroundColor Green
        $ok++
    }
    catch {
        Write-Host "  ERR $($entry.Name) - $($_.Exception.Message)" -ForegroundColor Red
        $errors++
    }
}

# Summary
Write-Host ""
Write-Host "---------------------------------------------" -ForegroundColor Gray
Write-Host "  Updated  : $ok" -ForegroundColor Green
if ($notFound -gt 0) {
    Write-Host "  Not found: $notFound  (name mismatch or no shift row in DB)" -ForegroundColor Yellow
}
if ($errors -gt 0) {
    Write-Host "  Errors   : $errors" -ForegroundColor Red
}
Write-Host "---------------------------------------------" -ForegroundColor Gray

if ($notFound -gt 0) {
    Write-Host ""
    Write-Host "Tip: list all FullName values in DB for today:" -ForegroundColor Gray
    Write-Host "  Invoke-RestMethod '$base/api/shifts?from=$today&to=$today' | Select-Object fullName | Sort-Object fullName" -ForegroundColor Gray
}
