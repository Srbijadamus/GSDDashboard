#Requires -Version 7.0
$CONNSTR = "Server=localhost\SQLEXPRESS;Database=ShiftKioskDB;Integrated Security=true;TrustServerCertificate=true;"
$conn = New-Object System.Data.SqlClient.SqlConnection $CONNSTR
$conn.Open()

function Q($sql, [hashtable]$p=@{}) {
    $cmd=$conn.CreateCommand(); $cmd.CommandText=$sql
    foreach ($k in $p.Keys) { $cmd.Parameters.AddWithValue($k,$p[$k])|Out-Null }
    $a=New-Object System.Data.SqlClient.SqlDataAdapter $cmd
    $d=New-Object System.Data.DataSet; $a.Fill($d)|Out-Null; return $d.Tables[0]
}

# Agent Base lista (primary_kid → name)
$agentBase = @(
    @{ kid='A81657'; name='Aakash Som' }
    @{ kid='A73442'; name='Abdulrahman Aldera' }
    @{ kid='A80925'; name='Adam Szilvagyi' }
    @{ kid='A81394'; name='Ahmad Dabbas' }
    @{ kid='A79697'; name='Amani Kedo' }
    @{ kid='A79647'; name='Amir Nassri' }
    @{ kid='A65019'; name='Angelika Weber' }
    @{ kid='A80245'; name='Anisha Nellikka Panikkan' }
    @{ kid='B29884'; name='Binod Dutta' }
    @{ kid='B29243'; name='Bishal Maharjan' }
    @{ kid='B25462'; name='Burak Kurtulmaz' }
    @{ kid='C43776'; name='Christian Martino' }
    @{ kid='C40658'; name='Christoph Ulatowski' }
    @{ kid='C44238'; name='Christos Kyrillidis' }
    @{ kid='D43156'; name='Dennis Markus' }
    @{ kid='D44372'; name='Dennis Obazee' }
    @{ kid='D45199'; name='Dmytro Shelikhov' }
    @{ kid='E26548'; name='Elaheh Ramzi' }
    @{ kid='E26615'; name='Elias Erdem' }
    @{ kid='E21423'; name='Erdal Coskun' }
    @{ kid='E26074'; name='Erik Goecks' }
    @{ kid='E22918'; name='Eyup Akyurek' }
    @{ kid='F23636'; name='Felix Spindler' }
    @{ kid='F23403'; name='Francois Sicot' }
    @{ kid='H34111'; name='Hamyaz Pathan' }
    @{ kid='H34487'; name='Hamza Forrousso' }
    @{ kid='H33776'; name='Hesham Montasser' }
    @{ kid='H29193'; name='Holger Kuhlmann' }
    @{ kid='H29101'; name='Holger Petzholdt' }
    @{ kid='I18521'; name='Ion Bodnariuc' }
    @{ kid='J51104'; name='Jannik Borner' }
    @{ kid='J59326'; name='Joel Broring' }
    @{ kid='J57815'; name='John Daniel Wendland' }
    @{ kid='K41008'; name='Kaan Arslan' }
    @{ kid='K40479'; name='Kamil Filipowicz' }
    @{ kid='K37144'; name='Kavinraj Pathmanathan' }
    @{ kid='K35422'; name='Kevin Heynen' }
    @{ kid='K41452'; name='Khaled Alali' }
    @{ kid='K40212'; name='Klaus Friedrich' }
    @{ kid='K41065'; name='Krishnendu Das' }
    @{ kid='L26183'; name='Lukas Schiefele' }
    @{ kid='M98548'; name='Mahboubeh Abdighara' }
    @{ kid='M100548'; name='Marcus Rusch' }
    @{ kid='M99927'; name='Mariusz Kozinski' }
    @{ kid='M73873'; name='Mark Bachmann' }
    @{ kid='M99198'; name='Merlin Voss' }
    @{ kid='M100791'; name='Mohamad Nasir Amany' }
    @{ kid='M99031'; name='Mohamed Khaled Mahmoud' }
    @{ kid='N23935'; name='Negin Bazmi' }
    @{ kid='O6319';  name='Olaf Wittenberg' }
    @{ kid='O9045';  name='$([char]0xD6)nder Arslan' }
    @{ kid='P37233'; name='Patrick Henschel' }
    @{ kid='R44968'; name='Rene Altmeyer' }
    @{ kid='S60574'; name="Sebastian H$([char]0xF6)ck" }
    @{ kid='S75427'; name='Sebastian Lewandowski' }
    @{ kid='S74973'; name='Senthuran Shanmugalingam' }
    @{ kid='S74279'; name='Sina Sidharthan' }
    @{ kid='S73217'; name='Suhrab Sadieqy' }
    @{ kid='T33358'; name='Tim Boger' }
    @{ kid='V18959'; name='Viktor Winter' }
    @{ kid='Y6505';  name='Yun Hee Oh' }
    # Bez primary_kid — match po imenu
    @{ kid=''; name='Ayten Karatas' }
    @{ kid=''; name='Ivan Leurs' }
    @{ kid=''; name='Yolanda Coppers' }          # Agent Base: "Jolanda Coppers Huijs"
    @{ kid=''; name='Mehmet Tigli' }
    @{ kid=''; name='Mohammad Al Masalma' }       # Agent Base: "Mohammad Al Masalama"
    @{ kid=''; name='Kai Erik Kumlehn' }          # Agent Base: "Kai Eric Kumlehn"
    @{ kid=''; name='Elliot van Staveren Kuste' } # Agent Base: "Elliot van Staveren Kuster"
    @{ kid=''; name='Michael Holz' }
    @{ kid=''; name='Stojnic Nebojsa' }
    @{ kid=''; name='Danny Bendig' }
    @{ kid=''; name='Ercan Akdeniz' }
    @{ kid=''; name='Jonathan Freudenthaler' }
    @{ kid=''; name='Michael M$([char]0xF6)ller' }
    @{ kid=''; name='Mitchel Sullivan' }
)

$ok=0; $inactive=0; $missing=0
$inactiveList=@(); $missingList=@(); $nameMismatch=@()

foreach ($a in $agentBase) {
    if ($a.kid -ne '') {
        $rows = Q "SELECT id, full_name, active FROM agents WHERE primary_kid=@k" @{'@k'=$a.kid}
    } else {
        $rows = Q "SELECT id, full_name, active FROM agents WHERE full_name=@n" @{'@n'=$a.name}
    }

    if ($rows.Rows.Count -eq 0) {
        $missing++
        $missingList += "  [NEMA]     kid=$($a.kid.PadRight(8))  $($a.name)"
    } else {
        $row = $rows.Rows[0]
        $dbName = $row.full_name
        if ($row.active -eq 1) {
            $ok++
            if ($dbName -ne $a.name -and $a.kid -ne '') {
                $nameMismatch += "  [IME]      $($a.name)  →  DB: $dbName"
            }
        } else {
            $inactive++
            $inactiveList += "  [NEAKTIVAN] kid=$($a.kid.PadRight(8))  DB: $dbName  (active=0)"
        }
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Agent Base provjera — rezultati" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ("  OK (aktivan):   {0}" -f $ok)      -ForegroundColor Green
Write-Host ("  Neaktivan:      {0}" -f $inactive) -ForegroundColor Yellow
Write-Host ("  Nedostaje:      {0}" -f $missing)  -ForegroundColor Red

if ($inactiveList.Count -gt 0) {
    Write-Host ""
    Write-Host "  ── NEAKTIVNI (u DB ali active=0) ──" -ForegroundColor Yellow
    $inactiveList | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
}
if ($missingList.Count -gt 0) {
    Write-Host ""
    Write-Host "  ── NEDOSTAJU (nisu u DB uopste) ──" -ForegroundColor Red
    $missingList | ForEach-Object { Write-Host $_ -ForegroundColor Red }
}
if ($nameMismatch.Count -gt 0) {
    Write-Host ""
    Write-Host "  ── RAZLIKA U IMENU (isti KID, razlicito ime) ──" -ForegroundColor Magenta
    $nameMismatch | ForEach-Object { Write-Host $_ -ForegroundColor Magenta }
}

Write-Host ""
$conn.Close()
