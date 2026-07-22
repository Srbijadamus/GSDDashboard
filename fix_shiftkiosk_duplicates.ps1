#Requires -Version 7.0
# fix_shiftkiosk_duplicates.ps1
# Rjesava duplikat Aman/Amani Kedo i pronalazi 5 visak agenata

$CONNSTR = "Server=localhost\SQLEXPRESS;Database=ShiftKioskDB;Integrated Security=true;TrustServerCertificate=true;"

function Write-Step([int]$n, [string]$msg) {
    Write-Host ""; Write-Host "  ══ Korak ${n}: $msg" -ForegroundColor Cyan
}
function Write-OK  ([string]$m) { Write-Host "  [OK]  $m" -ForegroundColor Green  }
function Write-Inf ([string]$m) { Write-Host "        $m" -ForegroundColor Gray   }
function Write-Warn([string]$m) { Write-Host "  [!!]  $m" -ForegroundColor Yellow }

function Invoke-Sql($c, $sql) {
    $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 30
    $a = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
    $d = New-Object System.Data.DataSet; $a.Fill($d) | Out-Null; return $d.Tables
}
function Invoke-NonQuery($c, $sql) {
    $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 30
    return $cmd.ExecuteNonQuery()
}

$conn = New-Object System.Data.SqlClient.SqlConnection $CONNSTR
$conn.Open()
Write-OK "Spojeno"

# ── KORAK 1: Pokazi sve aktivne agente koji imaju duplikat employee_id ────────
Write-Step 1 "Duplikati po employee_id"

$dups = Invoke-Sql $conn @'
SELECT employee_id,
       COUNT(*) AS cnt,
       STRING_AGG(CAST(id AS NVARCHAR) + ':' + first_name + ' ' + last_name, '  |  ')
           WITHIN GROUP (ORDER BY id) AS redovi
FROM agents
WHERE active = 1
  AND employee_id IS NOT NULL AND employee_id != ''
GROUP BY employee_id
HAVING COUNT(*) > 1;
'@

if ($dups[0].Rows.Count -eq 0) {
    Write-OK "Nema duplikata po employee_id"
} else {
    foreach ($r in $dups[0].Rows) {
        Write-Warn ("  employee_id={0}  ({1}x)  →  {2}" -f $r.employee_id, $r.cnt, $r.redovi)
    }
}

# ── KORAK 2: Fix Aman Kedo — deaktiviraj stariji red (manji id) ──────────────
Write-Step 2 "Deaktiviraj stari 'Aman Kedo' red (zadrzava noviji 'Amani Kedo')"

$rows = Invoke-NonQuery $conn @'
UPDATE agents
SET active = 0
WHERE first_name = 'Aman'
  AND last_name  = 'Kedo';
'@
Write-OK "Deaktivirano redova: $rows  (Aman Kedo → active=0)"

# ── KORAK 3: Pronaci aktivne agente koji NISU na aktuelnoj listi 120 ──────────
Write-Step 3 "Aktivni agenti koji nisu na aktuelnoj listi (visak)"

# Kompletna lista 120 — full_name vrijednosti
$activeFull = @(
    # WIC
    'Aakash Som','Abdulrahman Aldera','Adam Szilvagyi','Ahmad Dabbas','Amir Nassri',
    'Anisha Nellikka Panikkan','Ayten Karatas','Binod Dutta','Bishal Maharjan','Christian Martino',
    'Dennis Markus','Dmytro Shelikhov','Elaheh Ramzi','Erdal Coskun','Erik Goecks',
    'Eyup Akyurek','Felix Spindler','Francois Sicot','Hamyaz Pathan','Hamza Forrousso',
    'Hesham Montasser','Holger Kuhlmann','Holger Petzholdt','Ion Bodnariuc','Ivan Leurs',
    'Jannik Borner','Joel Broring','John Daniel Wendland','Kaan Arslan','Kamil Filipowicz',
    'Khaled Alali','Klaus Friedrich','Krishnendu Das','Lukas Schiefele','Mahboubeh Abdighara',
    'Marcus Rusch','Mariusz Kozinski','Mark Bachmann','Mehmet Tigli','Merlin Voss',
    'Mohamad Nasir Amany','Mohammad Al Masalma','Negin Bazmi','Olaf Wittenberg',
    ([char]0xD6+'nder Arslan'),'Rene Altmeyer',('Sebastian H'+[char]0xF6+'ck'),
    'Sebastian Lewandowski','Senthuran Shanmugalingam','Sina Sidharthan','Stojnic Nebojsa',
    'Suhrab Sadieqy','Tim Boger','Viktor Winter','Yun Hee Oh',
    # VWIC
    'Amani Kedo','Anifa Ngcongo','Gunter Dinkelmann','Isloodien Hurchem Lawrence',
    # Voice
    'Aleksandrina Dencheva','Anas Daba','Annabela Scavo','Arevig Ketenjian',
    'Asal Wardaastiani Azar','Boris Kostov','Christian Koch','Christian Pastors',
    'Danny Bendig','Darjusch Dropczinsky','Duc Quy Huynh','Elena Schlosser',
    'Elliot van Staveren Kuste','Eva-Liane Schliwa','Jonathan Freudenthaler','Kai Erik Kumlehn',
    'Kemal Sener','Kolja Christlieb','Lubomir Stoyanov','Martijn Brinks',
    ('Meik Sch'+[char]0xFC+'lgen'),'Mitchel Sullivan','Mitko Kilogramski','Mustafa Deveci',
    'Ralf Turski','Rene Raoul Aboikoni','Sam Alisha Metzner','Tarek Tabbara',
    'Timon Philippen','Tri Toan Nguyen','Veronika Kouwui','Vincent Grunzel',
    'Virgil Stelk','Walter Buxbaum','Yevgeni Frenkel','Yolanda Coppers',
    ('Zehra Sila G'+[char]0xF6+'rg'+[char]0xFC+'n'),
    # Other
    'Elias Erdem','Patrick Henschel','Kevin Haska','Burak Kurtulmaz','Anil Bedzeti',
    'Marko Bosnjak','Sharon Huber','Ahmed Hasanovic','Dominik Bajic','Erne Kis',
    'Kevin Heynen','Michael Holz','Christoph Ulatowski','Jessica Schlicht','Perim Rollin',
    'Adnan Lelic','Angelika Weber','Baschir Mahrufi','Javier Sang','Pascal Dutz',
    'Stephan Becker','Tim Nguyen','Victoria Scholz','Ercan Akdeniz'
)

# Dohvati sve aktivne iz DB
$allActive = Invoke-Sql $conn "SELECT id, first_name + ' ' + last_name AS ime, employee_id, team_leader FROM agents WHERE active=1 ORDER BY ime"

$extras = @()
foreach ($r in $allActive[0].Rows) {
    if ($r.ime -notin $activeFull) {
        $extras += $r
    }
}

if ($extras.Count -eq 0) {
    Write-OK "Nema visak agenata — tacno 120!"
} else {
    Write-Warn "$($extras.Count) aktivnih agenata koji NISU na listi:"
    foreach ($r in $extras) {
        Write-Inf ("  id={0,-6}  [{1,-12}]  {2,-35}  TL={3}" -f $r.id, $r.employee_id, $r.ime, $r.team_leader)
    }
    Write-Host ""
    Write-Host "  Deaktivirati ove agente? (D/n): " -ForegroundColor Yellow -NoNewline
    $ans = Read-Host
    if ($ans -eq '' -or $ans -match '^[Dd]') {
        foreach ($r in $extras) {
            $cmd = $conn.CreateCommand()
            $cmd.CommandText = "UPDATE agents SET active=0 WHERE id=@id"
            $cmd.Parameters.AddWithValue('@id', $r.id) | Out-Null
            $cmd.ExecuteNonQuery() | Out-Null
            Write-Inf ("  deaktiviran: {0}" -f $r.ime)
        }
        Write-OK "Deaktivirano $($extras.Count) agenata"
    } else {
        Write-Warn "Preskoceno — agenti ostaju aktivni"
    }
}

# ── KORAK 4: Finalni broj ─────────────────────────────────────────────────────
Write-Step 4 "Finalni broj aktivnih agenata"

$final = Invoke-Sql $conn "SELECT COUNT(*) AS n FROM agents WHERE active=1"
$n = $final[0].Rows[0].n
$color = if ($n -eq 120) { 'Green' } else { 'Yellow' }
Write-Host ("  Aktivnih agenata: {0}  (ocekivano: 120)" -f $n) -ForegroundColor $color

# VWIC finalna provjera
$vwic = Invoke-Sql $conn "SELECT first_name+' '+last_name AS ime FROM agents WHERE (location='VWIC' OR team_leader='VWIC') AND active=1 ORDER BY last_name"
Write-Host ""
Write-Host "  VWIC agenti ($($vwic[0].Rows.Count)):" -ForegroundColor Cyan
foreach ($r in $vwic[0].Rows) { Write-Host "    $($r.ime)" -ForegroundColor Cyan }

$conn.Close()
Write-Host ""
