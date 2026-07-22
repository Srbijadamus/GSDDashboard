#Requires -Version 7.0
# wic_roles_import.ps1 — Kreira wic_agent_roles tabelu i puni je po listi MAIN/BACKUP/REGIONAL

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
function Invoke-Scalar($c, $sql, [hashtable]$p = @{}) {
    $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 30
    foreach ($k in $p.Keys) { $cmd.Parameters.AddWithValue($k, $p[$k]) | Out-Null }
    $r = $cmd.ExecuteScalar()
    if ($null -eq $r -or $r -is [System.DBNull]) { return $null }
    return $r
}
function Invoke-CmdNQ($c, $sql, [hashtable]$p = @{}) {
    $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 30
    foreach ($k in $p.Keys) { $cmd.Parameters.AddWithValue($k, $p[$k]) | Out-Null }
    return $cmd.ExecuteNonQuery()
}

$conn = New-Object System.Data.SqlClient.SqlConnection $CONNSTR
$conn.Open()
Write-OK "Spojeno na ShiftKioskDB"

# ── KORAK 1: Kreira wic_agent_roles tabelu ───────────────────────────────────
Write-Step 1 "Kreira wic_agent_roles tabelu"
Invoke-NonQuery $conn @'
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='wic_agent_roles')
CREATE TABLE wic_agent_roles (
    id            INT IDENTITY PRIMARY KEY,
    wic_center_id INT NOT NULL REFERENCES wic_centers(id),
    agent_id      INT NOT NULL REFERENCES agents(id),
    role          NVARCHAR(20) NOT NULL,
    active        BIT NOT NULL DEFAULT 1,
    created_at    DATETIME DEFAULT GETDATE(),
    CONSTRAINT UQ_wic_agent_role UNIQUE (wic_center_id, agent_id, role)
)
'@ | Out-Null
Write-OK "wic_agent_roles: OK"

# ── KORAK 2: Dodaje Rendsburg (nedostaje u wic_centers) ──────────────────────
Write-Step 2 "Dodaje Rendsburg WIC centar"
$existsR = Invoke-Scalar $conn "SELECT COUNT(*) FROM wic_centers WHERE city='Rendsburg'"
if ($existsR -eq 0) {
    Invoke-NonQuery $conn @"
INSERT INTO wic_centers (city,location,address,monday,tuesday,wednesday,thursday,friday,saturday,sunday,active)
VALUES ('Rendsburg','DE~24768~Rendsburg~WIC Center','Rendsburg',
        '08:00 - 16:00','Closed','Closed','Closed','Closed','Closed','Closed',1)
"@ | Out-Null
    Write-OK "Rendsburg dodan (adresa i sati — azurirati manuelno)"
} else {
    Write-Inf "Rendsburg vec postoji"
}

# ── KORAK 3: Definisi sve assignmente ────────────────────────────────────────
Write-Step 3 "Priprema $([char]0x61)ssignmenata (MAIN/BACKUP/REGIONAL)"

# Format: wic city kao u bazi → role → full_name agenta
$assignments = @(
    # NORD / NORDWEST
    @{ city='Hamburg';                    role='MAIN';     agent='Bishal Maharjan' }
    @{ city='Hamburg';                    role='BACKUP';   agent='$([char]0xD6)nder Arslan' }
    @{ city='Hamburg';                    role='BACKUP';   agent='Elias Erdem' }

    @{ city='Hannover';                   role='MAIN';     agent='Olaf Wittenberg' }
    @{ city='Hannover';                   role='REGIONAL'; agent='Lukas Schiefele' }

    @{ city='Emmerthal';                  role='MAIN';     agent='Holger Petzholdt' }
    @{ city='Emmerthal';                  role='BACKUP';   agent='Olaf Wittenberg' }
    @{ city='Emmerthal';                  role='REGIONAL'; agent='Lukas Schiefele' }

    @{ city='Quickborn';                  role='MAIN';     agent='Amir Nassri' }

    @{ city='Rendsburg';                  role='MAIN';     agent='Hamza Forrousso' }
    @{ city='Rendsburg';                  role='BACKUP';   agent='Viktor Winter' }

    @{ city='Brokdorf';                   role='MAIN';     agent='Jannik Borner' }
    @{ city='Brokdorf';                   role='BACKUP';   agent='Viktor Winter' }

    @{ city='Stade';                      role='MAIN';     agent='Abdulrahman Aldera' }
    @{ city='Stade';                      role='MAIN';     agent='Joel Broring' }
    @{ city='Stade';                      role='BACKUP';   agent='Abdulrahman Aldera' }
    @{ city='Stade';                      role='BACKUP';   agent='Joel Broring' }
    @{ city='Stade';                      role='REGIONAL'; agent='Lukas Schiefele' }

    @{ city='Stadland';                   role='MAIN';     agent='Abdulrahman Aldera' }
    @{ city='Stadland';                   role='MAIN';     agent='Joel Broring' }
    @{ city='Stadland';                   role='BACKUP';   agent='Abdulrahman Aldera' }
    @{ city='Stadland';                   role='BACKUP';   agent='Joel Broring' }
    @{ city='Stadland';                   role='REGIONAL'; agent='Lukas Schiefele' }

    # OST
    @{ city='Berlin - Gaussstr.';         role='MAIN';     agent='Erik Goecks' }
    @{ city='Berlin - Gaussstr.';         role='BACKUP';   agent='Krishnendu Das' }

    @{ city='Berlin - Koepenicker Str.';  role='MAIN';     agent='Erik Goecks' }
    @{ city='Berlin - Koepenicker Str.';  role='BACKUP';   agent='Krishnendu Das' }

    @{ city='Fuerstenwalde';              role='MAIN';     agent='Klaus Friedrich' }
    @{ city='Fuerstenwalde';              role='BACKUP';   agent='Hamyaz Pathan' }

    @{ city='Potsdam';                    role='MAIN';     agent='Dennis Markus' }
    @{ city='Potsdam';                    role='BACKUP';   agent='Hamyaz Pathan' }

    @{ city='Demmin - Am Hanseufer';      role='MAIN';     agent='Rene Altmeyer' }
    @{ city='Demmin - Am Hanseufer';      role='BACKUP';   agent='Sebastian Lewandowski' }
    @{ city='Demmin - Am Hanseufer';      role='BACKUP';   agent='Hamyaz Pathan' }
    @{ city='Demmin - Am Hanseufer';      role='BACKUP';   agent='Viktor Winter' }

    @{ city='Demmin - Woldeforster Str';  role='MAIN';     agent='Rene Altmeyer' }
    @{ city='Demmin - Woldeforster Str';  role='BACKUP';   agent='Sebastian Lewandowski' }
    @{ city='Demmin - Woldeforster Str';  role='BACKUP';   agent='Hamyaz Pathan' }
    @{ city='Demmin - Woldeforster Str';  role='BACKUP';   agent='Viktor Winter' }

    @{ city='Halle';                      role='MAIN';     agent='Ion Bodnariuc' }
    @{ city='Halle';                      role='BACKUP';   agent='Felix Spindler' }

    @{ city='Markkleeberg';               role='MAIN';     agent='Ion Bodnariuc' }
    @{ city='Markkleeberg';               role='BACKUP';   agent='Felix Spindler' }

    # RUHRGEBIET / NRW
    @{ city='Essen - BP1';                role='MAIN';     agent='Holger Kuhlmann' }
    @{ city='Essen - BP1';                role='MAIN';     agent='Mark Bachmann' }
    @{ city='Essen - BP1';                role='MAIN';     agent='Erdal Coskun' }
    @{ city='Essen - BP1';                role='BACKUP';   agent='Kavinraj Pathmanathan' }
    @{ city='Essen - BP1';                role='BACKUP';   agent='Angelika Weber' }
    @{ city='Essen - BP1';                role='BACKUP';   agent='Kevin Heynen' }
    @{ city='Essen - BP1';                role='REGIONAL'; agent='Patrick Henschel' }

    @{ city='Essen - TK';                 role='MAIN';     agent='Kaan Arslan' }
    @{ city='Essen - TK';                 role='BACKUP';   agent='Kavinraj Pathmanathan' }
    @{ city='Essen - TK';                 role='BACKUP';   agent='Angelika Weber' }
    @{ city='Essen - TK';                 role='BACKUP';   agent='Kevin Heynen' }

    @{ city='Dortmund';                   role='MAIN';     agent='Christian Martino' }
    @{ city='Dortmund';                   role='BACKUP';   agent='Holger Kuhlmann' }
    @{ city='Dortmund';                   role='BACKUP';   agent='Angelika Weber' }
    @{ city='Dortmund';                   role='BACKUP';   agent="Sebastian H$([char]0xF6)ck" }

    @{ city='Muelheim';                   role='MAIN';     agent='Amani Kedo' }
    @{ city='Muelheim';                   role='REGIONAL'; agent='Kevin Heynen' }

    @{ city='Recklinghausen';             role='MAIN';     agent='Mahboubeh Abdighara' }
    @{ city='Recklinghausen';             role='BACKUP';   agent='Kai Erik Kumlehn' }
    @{ city='Recklinghausen';             role='BACKUP';   agent='Amani Kedo' }

    @{ city='Arnsberg';                   role='MAIN';     agent='Angelika Weber' }
    @{ city='Arnsberg';                   role='BACKUP';   agent='Amani Kedo' }

    @{ city='Wesel';                      role='MAIN';     agent='Angelika Weber' }
    @{ city='Wesel';                      role='BACKUP';   agent='Kavinraj Pathmanathan' }
    @{ city='Wesel';                      role='REGIONAL'; agent='Patrick Henschel' }

    @{ city='Muenster';                   role='MAIN';     agent='Mahboubeh Abdighara' }
    @{ city='Muenster';                   role='BACKUP';   agent='Kai Erik Kumlehn' }

    @{ city='Osnabrueck';                 role='MAIN';     agent='Mahboubeh Abdighara' }
    @{ city='Osnabrueck';                 role='BACKUP';   agent='Kai Erik Kumlehn' }

    @{ city='Neuss';                      role='MAIN';     agent='Yun Hee Oh' }
    @{ city='Neuss';                      role='BACKUP';   agent='Burak Kurtulmaz' }
    @{ city='Neuss';                      role='REGIONAL'; agent='Christoph Ulatowski' }

    # MITTE / NIEDERSACHSEN
    @{ city='Salzgitter';                 role='MAIN';     agent='Ahmad Dabbas' }
    @{ city='Salzgitter';                 role='MAIN';     agent='Aakash Som' }

    @{ city='Helmstedt';                  role='MAIN';     agent='Merlin Voss' }
    @{ city='Helmstedt';                  role='BACKUP';   agent='Senthuran Shanmugalingam' }

    # SUEDWEST
    @{ city='Saarbruecken';               role='MAIN';     agent='Hesham Montasser' }
    @{ city='Saarbruecken';               role='BACKUP';   agent='Suhrab Sadieqy' }

    @{ city='Trier';                      role='MAIN';     agent='Suhrab Sadieqy' }

    @{ city='Saffig';                     role='MAIN';     agent='Burak Kurtulmaz' }
    @{ city='Saffig';                     role='BACKUP';   agent='Negin Bazmi' }

    @{ city='Siegen';                     role='MAIN';     agent='Negin Bazmi' }

    @{ city='Neu-Isenburg';               role='MAIN';     agent='Elaheh Ramzi' }
    @{ city='Neu-Isenburg';               role='MAIN';     agent='Mohammad Al Masalma' }

    # SUEDDEUTSCHLAND
    @{ city='Muenchen';                   role='MAIN';     agent='Eyup Akyurek' }
    @{ city='Muenchen';                   role='BACKUP';   agent='Khaled Alali' }
    @{ city='Muenchen';                   role='BACKUP';   agent='Anisha Nellikka Panikkan' }

    @{ city='Landshut';                   role='MAIN';     agent='Khaled Alali' }
    @{ city='Landshut';                   role='BACKUP';   agent='Eyup Akyurek' }
    @{ city='Landshut';                   role='BACKUP';   agent='Adam Szilvagyi' }
    @{ city='Landshut';                   role='BACKUP';   agent='Sina Sidharthan' }
    @{ city='Landshut';                   role='BACKUP';   agent='Anisha Nellikka Panikkan' }

    @{ city='Essenbach';                  role='MAIN';     agent='John Daniel Wendland' }
    @{ city='Essenbach';                  role='MAIN';     agent='Adam Szilvagyi' }

    @{ city='Augsburg';                   role='MAIN';     agent='Kamil Filipowicz' }
    @{ city='Augsburg';                   role='BACKUP';   agent='Mohamad Nasir Amany' }
    @{ city='Augsburg';                   role='BACKUP';   agent='Sina Sidharthan' }

    @{ city='Pfaffenhofen';               role='MAIN';     agent='Binod Dutta' }
    @{ city='Pfaffenhofen';               role='BACKUP';   agent='Christos Kyrillidis' }
    @{ city='Pfaffenhofen';               role='BACKUP';   agent='Mohamad Nasir Amany' }
    @{ city='Pfaffenhofen';               role='BACKUP';   agent='Mehmet Tigli' }

    @{ city='Regensburg';                 role='MAIN';     agent='Marcus Rusch' }
    @{ city='Regensburg';                 role='BACKUP';   agent='Dmytro Shelikhov' }

    @{ city='Bamberg';                    role='MAIN';     agent='Mariusz Kozinski' }
    @{ city='Bamberg';                    role='BACKUP';   agent='Francois Sicot' }

    @{ city='Grafenrheinfeld';            role='MAIN';     agent='Tim Boger' }
    @{ city='Grafenrheinfeld';            role='REGIONAL'; agent='Lukas Schiefele' }

    # NIEDERLANDE
    @{ city='s-Hertogenbosch';            role='MAIN';     agent='Ayten Karatas' }
    @{ city='s-Hertogenbosch';            role='MAIN';     agent='Ivan Leurs' }
    @{ city='s-Hertogenbosch';            role='BACKUP';   agent='Yolanda Coppers' }

    @{ city='Zwolle';                     role='MAIN';     agent='Ivan Leurs' }
    @{ city='Zwolle';                     role='BACKUP';   agent='Elliot van Staveren Kuste' }
)

Write-Inf "Ukupno assignmenata: $($assignments.Count)"

# ── KORAK 4: Popuni wic_agent_roles ──────────────────────────────────────────
Write-Step 4 "Popunjava wic_agent_roles"

$inserted = 0; $skipped = 0; $warnings = 0

foreach ($a in $assignments) {
    $wcId = Invoke-Scalar $conn "SELECT id FROM wic_centers WHERE city=@c AND active=1" @{ '@c'=$a.city }
    if (-not $wcId) {
        Write-Warn "WIC centar nije nadjen: '$($a.city)'"
        $warnings++; continue
    }
    $agId = Invoke-Scalar $conn "SELECT id FROM agents WHERE full_name=@fn AND active=1" @{ '@fn'=$a.agent }
    if (-not $agId) {
        Write-Warn "Agent nije nadjen/aktivan: '$($a.agent)'"
        $warnings++; continue
    }
    $exists = Invoke-Scalar $conn @"
        SELECT COUNT(*) FROM wic_agent_roles
        WHERE wic_center_id=@wc AND agent_id=@ag AND role=@r
"@ @{ '@wc'=$wcId; '@ag'=$agId; '@r'=$a.role }
    if ($exists -gt 0) { $skipped++; continue }

    Invoke-CmdNQ $conn @"
        INSERT INTO wic_agent_roles (wic_center_id, agent_id, role)
        VALUES (@wc, @ag, @r)
"@ @{ '@wc'=$wcId; '@ag'=$agId; '@r'=$a.role } | Out-Null
    $inserted++
}

Write-OK "Ubaceno: $inserted  |  Vec postoji: $skipped  |  Upozorenja: $warnings"

# ── KORAK 5: Azurira wic_location na agentima (MAIN lokacije) ────────────────
Write-Step 5 "Azurira wic_location na agentima (iz MAIN uloge)"

$r = Invoke-NonQuery $conn @"
UPDATE a
SET a.wic_location = (
    SELECT STRING_AGG(wc.city, ' | ') WITHIN GROUP (ORDER BY wc.city)
    FROM wic_agent_roles war
    JOIN wic_centers wc ON wc.id = war.wic_center_id
    WHERE war.agent_id = a.id AND war.role = 'MAIN' AND war.active = 1
)
FROM agents a
WHERE a.active = 1
  AND EXISTS (
    SELECT 1 FROM wic_agent_roles war
    WHERE war.agent_id = a.id AND war.role = 'MAIN' AND war.active = 1
  )
"@
Write-OK "wic_location azurirano na $r agenata"

# ── KORAK 6: Rezime ───────────────────────────────────────────────────────────
Write-Step 6 "Rezime"

$byRole = Invoke-Sql $conn @'
SELECT role, COUNT(*) AS cnt
FROM wic_agent_roles WHERE active=1
GROUP BY role ORDER BY role
'@
foreach ($row in $byRole[0].Rows) {
    Write-Inf ("  {0,-10} : {1} redova" -f $row.role, $row.cnt)
}
$total = Invoke-Scalar $conn "SELECT COUNT(*) FROM wic_agent_roles WHERE active=1"
Write-OK "Ukupno u wic_agent_roles: $total"

$centers = Invoke-Scalar $conn "SELECT COUNT(DISTINCT wic_center_id) FROM wic_agent_roles WHERE active=1"
Write-OK "WIC centara sa assignmentima: $centers / 44"

$conn.Close()
Write-Host ""
Write-OK "Gotovo!"
