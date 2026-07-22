#Requires -Version 7.0
# update_wic_opening_days.ps1
# Dodaje opening_day kolonu, popunjava je, i ispravlja Halle+Quickborn

$CONNSTR = "Server=localhost\SQLEXPRESS;Database=ShiftKioskDB;Integrated Security=true;TrustServerCertificate=true;"
$conn = New-Object System.Data.SqlClient.SqlConnection $CONNSTR
$conn.Open()
Write-Host "[OK]  Spojeno" -ForegroundColor Green

function NQ($sql,[hashtable]$p=@{}) {
    $cmd=$conn.CreateCommand(); $cmd.CommandText=$sql
    foreach ($k in $p.Keys) { $cmd.Parameters.AddWithValue($k,$p[$k])|Out-Null }
    return $cmd.ExecuteNonQuery()
}
function Scalar($sql,[hashtable]$p=@{}) {
    $cmd=$conn.CreateCommand(); $cmd.CommandText=$sql
    foreach ($k in $p.Keys) { $cmd.Parameters.AddWithValue($k,$p[$k])|Out-Null }
    $r=$cmd.ExecuteScalar()
    if ($null -eq $r -or $r -is [System.DBNull]) { return $null }
    return $r
}

# ── KORAK 1: Dodaj opening_day kolonu ────────────────────────────────────────
Write-Host ""
Write-Host "  ══ Korak 1: Dodaje opening_day kolonu" -ForegroundColor Cyan
NQ @"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id=OBJECT_ID('wic_centers') AND name='opening_day')
ALTER TABLE wic_centers ADD opening_day NVARCHAR(200)
"@ | Out-Null
Write-Host "  [OK]  opening_day kolona: OK" -ForegroundColor Green

# ── Mapiranje: korisnicki naziv → DB city name ───────────────────────────────
# Format: @{ bu=<user name>; city=<DB city>; od=<opening_day text> }
$centers = @(
    @{ bu='Arnsberg';                city='Arnsberg';                  od='Di.' }
    @{ bu='Augsburg';                city='Augsburg';                  od='daily + Fr. 07:30-14:00' }
    @{ bu='Bamberg';                 city='Bamberg';                   od='daily + Fr. 1/2' }
    @{ bu='Berlin - Brueckenstrasse';city='Berlin - Brueckenstrasse';  od='Di.' }
    @{ bu='Berlin - Gaussstr.';      city='Berlin - Gaussstr.';        od='Mo.' }
    @{ bu='Berlin - Koepenicker';    city='Berlin - Koepenicker Str.'; od='' }
    @{ bu='Brokdorf';                city='Brokdorf';                  od='daily' }
    @{ bu='Demmin - Am Hanseufer';   city='Demmin - Am Hanseufer';     od='(Mo. Di. Mi. Do.) Vormittag' }
    @{ bu='Demmin - Woldeforster';   city='Demmin - Woldeforster Str'; od='(Di. Do.) Nachmittag' }
    @{ bu='Denbosch';                city='s-Hertogenbosch';           od='daily' }
    @{ bu='Dortmund';                city='Dortmund';                  od='daily' }
    @{ bu='Emmerthal';               city='Emmerthal';                 od='daily' }
    @{ bu='Essen BP1';               city='Essen - BP1';               od='daily *3' }
    @{ bu='Essen TK1';               city='Essen - TK';                od='daily' }
    @{ bu='Essenbach';               city='Essenbach';                 od='(Mo. Di. Mi. Do.) volltag + Fr. Vormittag' }
    @{ bu='Fuerstenwalde';           city='Fuerstenwalde';             od='(Mo. Mi.) volltag + (Di. Do.) Vormittag' }
    @{ bu='Grafenrheinfeld';         city='Grafenrheinfeld';           od='daily' }
    @{ bu='Halle';                   city='Halle';                     od='Mo. Di. Mi. Do.' }
    @{ bu='Hamburg';                 city='Hamburg';                   od='daily' }
    @{ bu='Hannover';                city='Hannover';                  od='(Di. Mi. Do.) volltag + (Mo. Fr.) Vormittag' }
    @{ bu='Helmstedt';               city='Helmstedt';                 od='(Mo. Di. Mi. Do.) volltag + Fr. Vormittag' }
    @{ bu='Landshut';                city='Landshut';                  od='Do.' }
    @{ bu='Markkleeberg';            city='Markkleeberg';              od='Mi. Do.' }
    @{ bu='Muelheim';                city='Muelheim';                  od='Do.' }
    @{ bu='Muenchen';                city='Muenchen';                  od='(Mo. Di. Mi. Do.) volltag + Fr. Vormittag' }
    @{ bu='Muenster';                city='Muenster';                  od='Di.' }
    @{ bu='Neu-Isenburg';            city='Neu-Isenburg';              od='daily' }
    @{ bu='Neuss';                   city='Neuss';                     od='Mi.' }
    @{ bu='Osnabrueck';              city='Osnabrueck';                od='Mo.' }
    @{ bu='Pfaffenhofen';            city='Pfaffenhofen';              od='(Mo. Di. Mi. Do.) volltag + Fr. Vormittag' }
    @{ bu='Potsdam';                 city='Potsdam';                   od='(Mo. Mi.) volltag + (Di. Do.) Vormittag' }
    @{ bu='Quickborn';               city='Quickborn';                 od='(Di. Mi. Do.) volltag' }
    @{ bu='Recklinghausen';          city='Recklinghausen';            od='Mi.' }
    @{ bu='Regensburg';              city='Regensburg';                od='(Mo. Di. Mi. Do.) volltag + Fr. Vormittag' }
    @{ bu='Rendsburg';               city='Rendsburg';                 od='Mo.' }
    @{ bu='Saarbruecken';            city='Saarbruecken';              od='daily' }
    @{ bu='Saffig';                  city='Saffig';                    od='Di.' }
    @{ bu='Salzgitter';              city='Salzgitter';                od='daily' }
    @{ bu='Siegen';                  city='Siegen';                    od='Mi.' }
    @{ bu='Stade';                   city='Stade';                     od='Mo. Do.' }
    @{ bu='Stadland';                city='Stadland';                  od='(Mo. Di. Mi. Do.) volltag + Fr. Vormittag' }
    @{ bu='Trier';                   city='Trier';                     od='Do.' }
    @{ bu='Wesel';                   city='Wesel';                     od='Do.' }
    @{ bu='Zwolle';                  city='Zwolle';                    od='Mo. Mi.' }
)

# ── KORAK 2: Azuriraj opening_day za svaki centar ────────────────────────────
Write-Host ""
Write-Host "  ══ Korak 2: Azurira opening_day ($($centers.Count) centara)" -ForegroundColor Cyan

$ok=0; $warn=0
foreach ($c in $centers) {
    $id = Scalar "SELECT id FROM wic_centers WHERE city=@city AND active=1" @{'@city'=$c.city}
    if (-not $id) {
        Write-Host ("  [!!]  Nije nadjen u DB: '{0}'" -f $c.city) -ForegroundColor Yellow
        $warn++; continue
    }
    NQ "UPDATE wic_centers SET opening_day=@od WHERE id=@id" @{'@od'=$c.od; '@id'=$id} | Out-Null
    $ok++
}
Write-Host ("  [OK]  Azurirano: {0}  |  Upozorenja: {1}" -f $ok,$warn) -ForegroundColor Green

# ── KORAK 3: Ispravke dana — Halle i Quickborn ───────────────────────────────
Write-Host ""
Write-Host "  ══ Korak 3: Ispravlja Halle i Quickborn" -ForegroundColor Cyan

# Halle: dodaj Wednesday i Thursday (bili Closed, treba biti otvoreni)
$r = NQ @"
UPDATE wic_centers
SET wednesday='08:00 - 16:00', thursday='08:00 - 16:00'
WHERE city='Halle' AND wednesday='Closed'
"@
Write-Host ("  [OK]  Halle: wednesday + thursday otvoreni ({0} red)" -f $r) -ForegroundColor Green

# Quickborn: zatvori Monday (bio 08:00-16:30, nova tablica: Di/Mi/Do samo)
$r = NQ @"
UPDATE wic_centers
SET monday='Closed'
WHERE city='Quickborn' AND monday != 'Closed'
"@
Write-Host ("  [OK]  Quickborn: monday zatvoren ({0} red)" -f $r) -ForegroundColor Green

# ── KORAK 4: Upozorenja ───────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ══ Korak 4: Napomene" -ForegroundColor Cyan
Write-Host "        Berlin - Koepenicker: opening_day ostavljen prazan (Di. podrazumijevano iz rasporeda)" -ForegroundColor Gray
Write-Host "        'Training': ne postoji u DB — preskocen" -ForegroundColor Gray

# ── KORAK 5: Provjera ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ══ Korak 5: Provjera" -ForegroundColor Cyan

$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
SELECT city, opening_day, monday, tuesday, wednesday, thursday, friday
FROM wic_centers WHERE active=1
ORDER BY city
"@
$a = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
$d = New-Object System.Data.DataSet; $a.Fill($d)|Out-Null

Write-Host ("  {0,-35} {1,-40} {2,-6} {3,-6} {4,-6} {5,-6} {6}" -f "City","opening_day","Mo","Di","Mi","Do","Fr") -ForegroundColor Cyan
Write-Host ("  " + "-"*110) -ForegroundColor DarkGray
foreach ($row in $d.Tables[0].Rows) {
    $mo = if ($row.monday  -eq 'Closed') {'—'} else {'✓'}
    $di = if ($row.tuesday  -eq 'Closed') {'—'} else {'✓'}
    $mi = if ($row.wednesday-eq 'Closed') {'—'} else {'✓'}
    $do = if ($row.thursday -eq 'Closed') {'—'} else {'✓'}
    $fr = if ($row.friday   -eq 'Closed') {'—'} else {'✓'}
    $od = if ($row.opening_day -eq $null -or $row.opening_day -eq '') {'(prazan)'} else {$row.opening_day}
    Write-Host ("  {0,-35} {1,-40} {2,-6} {3,-6} {4,-6} {5,-6} {6}" -f $row.city, $od, $mo,$di,$mi,$do,$fr)
}

$conn.Close()
Write-Host ""
Write-Host "  [OK]  Gotovo!" -ForegroundColor Green
