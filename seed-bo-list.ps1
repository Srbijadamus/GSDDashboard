$date = "2026-07-15"
$url  = "http://localhost:5000/api/bo-list"

$agents = @(
    @{ employeeName="Sina Sidharthan";      shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Baschir Mahrufi";      shiftStart="07:00"; shiftEnd="16:00"; note=$null },
    @{ employeeName="Javier Sang";          shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Stefan Becker";        shiftStart="07:00"; shiftEnd="16:00"; note=$null },
    @{ employeeName="Adnan Lelic";          shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Kai Erik Kumlehn";     shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Lukas Schiefele";      shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Anifa Ngcongo";        shiftStart="10:00"; shiftEnd="17:00"; note="Erst Tickets abarbeiten" },
    @{ employeeName="Sebastian Lewandowski";shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Elaheh Ramzi";         shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Marko Bosnjak";        shiftStart="08:00"; shiftEnd="17:00"; note="Newjoiner" },
    @{ employeeName="Hamyaz Pathan";        shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Suhrab Sadieqy";       shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Hamza Forrouso";       shiftStart="08:00"; shiftEnd="16:30"; note=$null },
    @{ employeeName="Aakash Som";           shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Ion Bodnariuc";        shiftStart="08:00"; shiftEnd="17:00"; note="Enviam" },
    @{ employeeName="Perim Rollin";         shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Tim Nguyen";           shiftStart="08:00"; shiftEnd="17:00"; note=$null },
    @{ employeeName="Erik Goecks";          shiftStart="08:00"; shiftEnd="17:00"; note=$null }
)

foreach ($a in $agents) {
    $body = @{ date=$date; employeeName=$a.employeeName; shiftStart=$a.shiftStart; shiftEnd=$a.shiftEnd; note=$a.note } | ConvertTo-Json
    Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" | Out-Null
    Write-Host "  + $($a.employeeName)" -ForegroundColor Green
}

Write-Host "`nGotovo — $($agents.Count) agenata uneseno za $date" -ForegroundColor Cyan
