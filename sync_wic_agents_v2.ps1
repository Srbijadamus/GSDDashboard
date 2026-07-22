#Requires -Version 7.0
# sync_wic_agents_v2.ps1
# Azurira employee_id, wic_location, team_leader po novoj WIC tablici
# Dodaje Michael Mollera, reaktivira Kavinraja, postavlja RWE za Henschela

$CONNSTR = "Server=localhost\SQLEXPRESS;Database=ShiftKioskDB;Integrated Security=true;TrustServerCertificate=true;"

function Write-Step([int]$n, [string]$msg) {
    Write-Host ""; Write-Host "  ══ Korak ${n}: $msg" -ForegroundColor Cyan
}
function Write-OK  ([string]$m) { Write-Host "  [OK]  $m" -ForegroundColor Green  }
function Write-Inf ([string]$m) { Write-Host "        $m" -ForegroundColor Gray   }
function Write-Warn([string]$m) { Write-Host "  [!!]  $m" -ForegroundColor Yellow }
function Write-Err ([string]$m) { Write-Host "  [XX]  $m" -ForegroundColor Red    }

$conn = New-Object System.Data.SqlClient.SqlConnection $CONNSTR
$conn.Open()
Write-OK "Spojeno na ShiftKioskDB"

function Scalar($sql, [hashtable]$p=@{}) {
    $cmd=$conn.CreateCommand(); $cmd.CommandText=$sql
    foreach ($k in $p.Keys) { $cmd.Parameters.AddWithValue($k,$p[$k])|Out-Null }
    $r=$cmd.ExecuteScalar()
    if ($null -eq $r -or $r -is [System.DBNull]) { return $null }
    return $r
}
function NQ($sql, [hashtable]$p=@{}) {
    $cmd=$conn.CreateCommand(); $cmd.CommandText=$sql
    foreach ($k in $p.Keys) { $cmd.Parameters.AddWithValue($k,$p[$k])|Out-Null }
    return $cmd.ExecuteNonQuery()
}

# Helper: normalizuj wic_location string (trim, konzistentni razmaci oko pipe)
function Norm([string]$s) {
    if (!$s -or $s -eq '0') { return $null }
    return ($s -replace '\s*\|\s*', ' | ').Trim(' |')
}

# ── Podaci iz tablice (eid='RWE' ili '' = posebni slucajevi) ──────────────────
# Format: eid, dbName (tacno ime u DB), wic, tl
$wicTable = @(
    @{ eid='9130648'; dbName='Aakash Som';              wic='Salzgitter | Hannover';                                  tl='Karlo Coric' }
    @{ eid='9125519'; dbName='Abdulrahman Aldera';       wic='Stade | Standland';                                      tl='Ion Ciuceanu' }
    @{ eid='9126881'; dbName='Adam Szilvagyi';           wic='Essenbach | Landshut';                                   tl='Ion Ciuceanu' }
    @{ eid='9129441'; dbName='Ahmad Dabbas';             wic='Salzgitter';                                             tl='Karlo Coric' }
    @{ eid='9120970'; dbName='Amani Kedo';               wic="M$([char]0xFC)lheim | Recklinghausen | Arnsberg | Essen | Dortmund"; tl='Tobias Rossberg' }
    @{ eid='9120965'; dbName='Amir Nassri';              wic='Quickborn';                                              tl='Delia Panaitescu' }
    @{ eid='9074345'; dbName='Angelika Weber';           wic="Wesel | Dortmund | Essen | Stade | Stadtland | Essenbach | Grafenrheinfeld | Arnsberg | Brokdorf"; tl='Tobias Rossberg' }
    @{ eid='9124145'; dbName='Anisha Nellikka Panikkan'; wic="M$([char]0xFC)nchen | Landshut";                        tl='Tobias Rossberg' }
    @{ eid='9047339'; dbName='Ayten Karatas';            wic='Denbosch';                                               tl='Jaroslaw Brzeszkiewicz' }
    @{ eid='9129428'; dbName='Binod Dutta';              wic='Pfaffenhofen';                                           tl='Tobias Rossberg' }
    @{ eid='9117834'; dbName='Bishal Maharjan';          wic='Hamburg | Quickborn | Rendsburg';                        tl='Delia Panaitescu' }
    @{ eid='9074352'; dbName='Burak Kurtulmaz';          wic='Saffig | Neuss';                                         tl='Tobias Rossberg' }
    @{ eid='9124688'; dbName='Christian Martino';        wic='Essen | Dortmund';                                       tl='Oliver Schleusen' }
    @{ eid='9085138'; dbName='Christoph Ulatowski';      wic='Neuss';                                                  tl='Tobias Rossberg' }
    @{ eid='9107615'; dbName='Dennis Markus';            wic='Potsdam';                                                tl='Delia Panaitescu' }
    @{ eid='9130657'; dbName='Dmytro Shelikhov';         wic='Regensburg | Pfaffenhofen';                              tl='Tobias Rossberg' }
    @{ eid='9126880'; dbName='Elaheh Ramzi';             wic='Neu-Isenburg';                                           tl='Oliver Schleusen' }
    @{ eid='9122676'; dbName='Erdal Coskun';             wic='Essen';                                                  tl='Oliver Schleusen' }
    @{ eid='9074431'; dbName='Erik Goecks';              wic="Berlin | Potsdam | F$([char]0xFC)rstenwalde";            tl='Delia Panaitescu' }
    @{ eid='9128148'; dbName='Eyup Akyurek';             wic="M$([char]0xFC)nchen";                                    tl='Ion Ciuceanu' }
    @{ eid='9128153'; dbName='Francois Sicot';           wic='Bamberg | Regensburg';                                   tl='Tobias Rossberg' }
    @{ eid='9122679'; dbName='Hamyaz Pathan';            wic="Potsdam | F$([char]0xFC)rstenwalde | Berlin | Demmin | Halle | Markkleeberg"; tl='Delia Panaitescu' }
    @{ eid='9130650'; dbName='Hamza Forrousso';          wic='Rendsburg';                                              tl='Delia Panaitescu' }
    @{ eid='9112563'; dbName='Hesham Montasser';         wic="Saarbr$([char]0xFC)cken";                                tl='Oliver Schleusen' }
    @{ eid='9122674'; dbName='Holger Kuhlmann';          wic='Essen | Dortmund';                                       tl='Oliver Schleusen' }
    @{ eid='9125517'; dbName='Holger Petzholdt';         wic='Emmerthal | Essenbach | Grafenrheinfeld';                tl='Ion Ciuceanu' }
    @{ eid='9126883'; dbName='Ion Bodnariuc';            wic='Halle | Markkleeberg';                                   tl='Ion Ciuceanu' }
    @{ eid='9074512'; dbName='Ivan Leurs';               wic='Denbosch | Zwolle';                                      tl='Jaroslaw Brzeszkiewicz' }
    @{ eid='9126874'; dbName='Jannik Borner';            wic='Brokdorf';                                               tl='Ion Ciuceanu' }
    @{ eid='9125516'; dbName='Joel Broring';             wic='Stade | Standland';                                      tl='Ion Ciuceanu' }
    @{ eid='9129427'; dbName='John Daniel Wendland';     wic='Essenbach';                                              tl='Ion Ciuceanu' }
    @{ eid='9120971'; dbName='Kaan Arslan';              wic='Essen';                                                  tl='Oliver Schleusen' }
    @{ eid='9112561'; dbName='Kamil Filipowicz';         wic='Augsburg';                                               tl='Tobias Rossberg' }
    @{ eid='9128149'; dbName='Kavinraj Pathmanathan';    wic='Essen | Wesel';                                          tl='Ion Ciuceanu' }
    @{ eid='9074526'; dbName='Kevin Heynen';             wic="Essen | M$([char]0xFC)lheim";                            tl='Oliver Schleusen' }
    @{ eid='9126882'; dbName='Khaled Alali';             wic="Landshut | M$([char]0xFC)nchen | Augsburg";              tl='Ion Ciuceanu' }
    @{ eid='9107616'; dbName='Klaus Friedrich';          wic="F$([char]0xFC)rstenwalde";                               tl='Delia Panaitescu' }
    @{ eid='9084156'; dbName='Krishnendu Das';           wic="Berlin | Brandenburg | F$([char]0xFC)rstenwalde | Potsdam"; tl='Delia Panaitescu' }
    @{ eid='9130643'; dbName='Lukas Schiefele';          wic='Hannover | Emmerthal | Grafenrheinfeld | Stade | Stadland | Helmstedt | Salzgitter'; tl='Ion Ciuceanu' }
    @{ eid='9121951'; dbName='Mahboubeh Abdighara';      wic="M$([char]0xFC)nster | Osnabr$([char]0xFC)ck | Recklinghausen"; tl='Tobias Rossberg' }
    @{ eid='9130649'; dbName='Marcus Rusch';             wic='Regensburg';                                             tl='Tobias Rossberg' }
    @{ eid='9128157'; dbName='Mariusz Kozinski';         wic='Bamberg';                                                tl='Tobias Rossberg' }
    @{ eid='9122675'; dbName='Mark Bachmann';            wic='Essen';                                                  tl='Oliver Schleusen' }
    @{ eid='9124697'; dbName='Merlin Voss';              wic='Helmstedt | Salzgitter | Hannover';                      tl='Karlo Coric' }
    @{ eid='9090513'; dbName='Michael Holz';             wic='Halle';                                                  tl='Karlo Coric' }
    @{ eid='9126886'; dbName='Negin Bazmi';              wic="Siegen | Saffig | Trier | Saarbr$([char]0xFC)cken";      tl='Tobias Rossberg' }
    @{ eid='9124144'; dbName='Olaf Wittenberg';          wic='Hannover | Emmerthal';                                   tl='Karlo Coric' }
    @{ eid='9120980'; dbName='Rene Altmeyer';            wic='Demmin';                                                 tl='Delia Panaitescu' }
    @{ eid='9074573'; dbName="Sebastian H$([char]0xF6)ck"; wic='Dortmund';                                            tl='Tobias Rossberg' }
    @{ eid='9129429'; dbName='Senthuran Shanmugalingam'; wic='Helmstedt';                                              tl='Karlo Coric' }
    @{ eid='9124691'; dbName='Sina Sidharthan';          wic="Pfaffenhofen | Landshut | M$([char]0xFC)nchen | Bamberg | Regensburg"; tl='Tobias Rossberg' }
    @{ eid='9074466'; dbName='Stojnic Nebojsa';          wic=$null;                                                    tl='Ion Ciuceanu' }
    @{ eid='9117836'; dbName='Suhrab Sadieqy';           wic="Trier | Saarbr$([char]0xFC)cken";                        tl='Oliver Schleusen' }
    @{ eid='9125521'; dbName='Tim Boger';                wic='Grafenrheinfeld';                                        tl='Ion Ciuceanu' }
    @{ eid='9044352'; dbName='Yolanda Coppers';          wic='Denbosch';                                               tl='Jaroslaw Brzeszkiewicz' }
    @{ eid='9106138'; dbName='Yun Hee Oh';               wic='Neuss';                                                  tl='Tobias Rossberg' }
    @{ eid='9132851'; dbName='Elliot van Staveren Kuste'; wic='Zwolle';                                                tl='Jaroslaw Brzeszkiewicz' }
    @{ eid='9133995'; dbName='Viktor Winter';            wic='Rendsburg | Brokdorf | Demmin | Hamburg';                tl='Delia Panaitescu' }
    @{ eid='9132075'; dbName='Felix Spindler';           wic='Halle | Markkleeberg';                                   tl='Karlo Coric' }
    @{ eid='9132077'; dbName='Mohamad Nasir Amany';      wic='Augsburg | Pfaffenhofen';                                tl='Tobias Rossberg' }
    @{ eid='9132079'; dbName='Sebastian Lewandowski';    wic=$null;                                                    tl='Delia Panaitescu' }
    @{ eid='9133998'; dbName="$([char]0xD6)nder Arslan"; wic='Hamburg';                                               tl='Delia Panaitescu' }
    @{ eid='9135516'; dbName='Mehmet Tigli';             wic='Pfaffenhofen';                                           tl='Tobias Rossberg' }
    @{ eid='9135517'; dbName='Mohammad Al Masalma';      wic='Neu-Isenburg';                                           tl='Oliver Schleusen' }
    @{ eid='RWE';     dbName='Patrick Henschel';         wic='Essen | Wesel';                                          tl='Ion Ciuceanu' }
    @{ eid='9133997'; dbName='Kai Erik Kumlehn';         wic="M$([char]0xFC)nster | Osnabr$([char]0xFC)ck | Recklinghausen"; tl='Tobias Rossberg' }
    @{ eid='9132070'; dbName='Mitchel Sullivan';         wic='Denbosch | Zwolle';                                      tl='Jaroslaw Brzeszkiewicz' }
    @{ eid='9119463'; dbName='Jonathan Freudenthaler';   wic="Arnsberg | M$([char]0xFC)lheim";                         tl='Oliver Schleusen' }
)

# ── KORAK 1: Azuriraj sve agente iz tablice ──────────────────────────────────
Write-Step 1 "Azurira agente po novoj WIC tablici ($($wicTable.Count) redova)"

$updated=0; $notFound=0; $skipped=0

foreach ($a in $wicTable) {
    # Trazi po employee_id
    $id = Scalar "SELECT id FROM agents WHERE employee_id=@e AND active IN (0,1)" @{'@e'=$a.eid}
    # Ako nije nadjen po eid, trazi po imenu
    if (-not $id) {
        $id = Scalar "SELECT id FROM agents WHERE full_name=@n AND active IN (0,1)" @{'@n'=$a.dbName}
    }
    if (-not $id) {
        Write-Warn "Nije nadjen: eid=$($a.eid)  '$($a.dbName)'"
        $notFound++; continue
    }

    $wicVal = $a.wic   # null = ne azuriraj wic_location
    $sql = if ($null -ne $wicVal) {
        "UPDATE agents SET employee_id=@e, wic_location=@w, team_leader=@t WHERE id=@id"
    } else {
        "UPDATE agents SET employee_id=@e, team_leader=@t WHERE id=@id"
    }
    $params = if ($null -ne $wicVal) {
        @{'@e'=$a.eid; '@w'=$wicVal; '@t'=$a.tl; '@id'=$id}
    } else {
        @{'@e'=$a.eid; '@t'=$a.tl; '@id'=$id}
    }
    NQ $sql $params | Out-Null
    $updated++
}

Write-OK "Azurirano: $updated  |  Nije nadjen: $notFound"

# ── KORAK 2: Reaktiviraj Kavinraj Pathmanathan ───────────────────────────────
Write-Step 2 "Reaktivira Kavinraj Pathmanathan"
$kavId = Scalar "SELECT id FROM agents WHERE full_name='Kavinraj Pathmanathan'"
if ($kavId) {
    $wasActive = Scalar "SELECT active FROM agents WHERE id=@id" @{'@id'=$kavId}
    NQ "UPDATE agents SET active=1, employee_id='9128149', wic_location='Essen | Wesel', team_leader='Ion Ciuceanu' WHERE id=@id" @{'@id'=$kavId} | Out-Null
    $status = if ($wasActive -eq 1) { "vec aktivan" } else { "reaktiviran (bio active=0)" }
    Write-OK "Kavinraj Pathmanathan — $status"
} else {
    Write-Warn "Kavinraj nije nadjen u DB"
}

# ── KORAK 3: Dodaj Michael Mollera (novi agent) ──────────────────────────────
Write-Step 3 "Dodaje Michael Mollera (novi WIC agent)"
$mollId = Scalar "SELECT id FROM agents WHERE employee_id='9137250'"
if ($mollId) {
    Write-Inf "Vec postoji (id=$mollId) — samo azurira"
    NQ "UPDATE agents SET first_name='Michael', last_name=@ln, wic_location='Demmin', active=1 WHERE id=@id" @{
        '@ln'="M$([char]0xF6)ller"; '@id'=$mollId
    } | Out-Null
} else {
    $byName = Scalar "SELECT id FROM agents WHERE full_name=@n" @{'@n'="Michael M$([char]0xF6)ller"}
    if ($byName) {
        Write-Inf "Nadjen po imenu (id=$byName) — azurira employee_id"
        NQ "UPDATE agents SET employee_id='9137250', wic_location='Demmin', active=1 WHERE id=@id" @{'@id'=$byName} | Out-Null
    } else {
        NQ @"
INSERT INTO agents (first_name, last_name, team_leader, employee_id, wic_location, engagement, active)
VALUES ('Michael', @ln, '', '9137250', 'Demmin', 'WIC', 1)
"@ @{'@ln'="M$([char]0xF6)ller"} | Out-Null
        Write-OK "Michael Moller — INSERT (novi agent, employee_id=9137250, WIC: Demmin)"
    }
}

# ── KORAK 4: Finalni brojevi ─────────────────────────────────────────────────
Write-Step 4 "Kontrola"
$total   = Scalar "SELECT COUNT(*) FROM agents WHERE active=1"
$wicOnly = Scalar "SELECT COUNT(*) FROM agents WHERE active=1 AND wic_location IS NOT NULL AND wic_location != '' AND wic_location != '0'"
$kavCheck= Scalar "SELECT active FROM agents WHERE full_name='Kavinraj Pathmanathan'"
Write-OK "Aktivnih agenata: $total"
Write-OK "Sa WIC lokacijom: $wicOnly"
Write-Inf "Kavinraj active=$kavCheck"

$conn.Close()
Write-Host ""
Write-OK "Gotovo!"
