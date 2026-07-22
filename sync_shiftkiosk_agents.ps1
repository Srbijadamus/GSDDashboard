#Requires -Version 7.0
# sync_shiftkiosk_agents.ps1 — ShiftKiosk agent sync  2026-07-14
# Source: PS1_71_ExportActiveAgents.ps1  (120 aktivnih agenata)
# DB:     ShiftKioskDB @ localhost\SQLEXPRESS  (trusted connection)
#
# Sto radi:
#   Korak 1 — konekcija
#   Korak 2 — stanje prije
#   Korak 3 — MERGE po employee_id (118 agenata sa numerickim ID)
#   Korak 4 — UPDATE po full_name za E26615/P37233 (nemaju employee_id)
#   Korak 5 — VWIC team_leader (odvoji VWIC od ostalih u TL filteru)
#   Korak 6 — deaktivacija agenata koji nisu vise na listi
#   Korak 7 — stanje poslije + provjera VWIC

$ErrorActionPreference = 'Stop'

$SERVER  = 'localhost\SQLEXPRESS'
$DB      = 'ShiftKioskDB'
$CONNSTR = "Server=$SERVER;Database=$DB;Integrated Security=true;TrustServerCertificate=true;"

# ── helpers ──────────────────────────────────────────────────────────────────

function Write-Step([int]$n, [string]$msg) {
    Write-Host ""
    Write-Host "  ══ Korak ${n}: $msg" -ForegroundColor Cyan
}
function Write-OK  ([string]$m) { Write-Host "  [OK]  $m" -ForegroundColor Green  }
function Write-Inf ([string]$m) { Write-Host "        $m" -ForegroundColor Gray   }
function Write-Warn([string]$m) { Write-Host "  [!!]  $m" -ForegroundColor Yellow }
function Write-Err ([string]$m) { Write-Host "  [XX]  $m" -ForegroundColor Red    }

function Invoke-Sql([System.Data.SqlClient.SqlConnection]$c, [string]$sql) {
    $cmd            = $c.CreateCommand()
    $cmd.CommandText    = $sql
    $cmd.CommandTimeout = 60
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
    $ds      = New-Object System.Data.DataSet
    $adapter.Fill($ds) | Out-Null
    return $ds.Tables
}

function Invoke-NonQuery([System.Data.SqlClient.SqlConnection]$c, [string]$sql) {
    $cmd             = $c.CreateCommand()
    $cmd.CommandText     = $sql
    $cmd.CommandTimeout  = 60
    return $cmd.ExecuteNonQuery()
}

# ── 120 aktivnih agenata po kategoriji ───────────────────────────────────────
# Format: @(employee_id, first_name, last_name, category, engagement)
# category -> team_leader za VWIC/Voice/Other (novi agenti)
#           -> za WIC agente koji vec postoje, team_leader se ne dira

$AGENTS = @(
    # WIC (55) ─────────────────────────────────────────────────────────────
    ,@('9130648','Aakash',            'Som',                 'WIC',  '')
    ,@('9125519','Abdulrahman',       'Aldera',              'WIC',  '')
    ,@('9126881','Adam',              'Szilvagyi',           'WIC',  '')
    ,@('9129441','Ahmad',             'Dabbas',              'WIC',  '')
    ,@('9120965','Amir',              'Nassri',              'WIC',  '')
    ,@('9124145','Anisha Nellikka',   'Panikkan',            'WIC',  '')
    ,@('9047339','Ayten',             'Karatas',             'WIC',  '')
    ,@('9129428','Binod',             'Dutta',               'WIC',  '')
    ,@('9117834','Bishal',            'Maharjan',            'WIC',  '')
    ,@('9124688','Christian',         'Martino',             'WIC',  '')
    ,@('9107615','Dennis',            'Markus',              'WIC',  '')
    ,@('9130657','Dmytro',            'Shelikhov',           'WIC',  '')
    ,@('9126880','Elaheh',            'Ramzi',               'WIC',  '')
    ,@('9122676','Erdal',             'Coskun',              'WIC',  '')
    ,@('9074431','Erik',              'Goecks',              'WIC',  '')
    ,@('9128148','Eyup',              'Akyurek',             'WIC',  '')
    ,@('9132075','Felix',             'Spindler',            'WIC',  '')
    ,@('9128153','Francois',          'Sicot',               'WIC',  '')
    ,@('9122679','Hamyaz',            'Pathan',              'WIC',  '')
    ,@('9130650','Hamza',             'Forrousso',           'WIC',  '')
    ,@('9112563','Hesham',            'Montasser',           'WIC',  '')
    ,@('9122674','Holger',            'Kuhlmann',            'WIC',  '')
    ,@('9125517','Holger',            'Petzholdt',           'WIC',  '')
    ,@('9126883','Ion',               'Bodnariuc',           'WIC',  '')
    ,@('9074512','Ivan',              'Leurs',               'WIC',  '')
    ,@('9126874','Jannik',            'Borner',              'WIC',  '')
    ,@('9125516','Joel',              'Broring',             'WIC',  '')
    ,@('9129427','John Daniel',       'Wendland',            'WIC',  '')
    ,@('9120971','Kaan',              'Arslan',              'WIC',  '')
    ,@('9112561','Kamil',             'Filipowicz',          'WIC',  '')
    ,@('9126882','Khaled',            'Alali',               'WIC',  '')
    ,@('9107616','Klaus',             'Friedrich',           'WIC',  '')
    ,@('9084156','Krishnendu',        'Das',                 'WIC',  '')
    ,@('9130643','Lukas',             'Schiefele',           'WIC',  '')
    ,@('9121951','Mahboubeh',         'Abdighara',           'WIC',  '')
    ,@('9130649','Marcus',            'Rusch',               'WIC',  '')
    ,@('9128157','Mariusz',           'Kozinski',            'WIC',  '')
    ,@('9122675','Mark',              'Bachmann',            'WIC',  '')
    ,@('9135516','Mehmet',            'Tigli',               'WIC',  '')
    ,@('9124697','Merlin',            'Voss',                'WIC',  '')
    ,@('9132077','Mohamad Nasir',     'Amany',               'WIC',  '')
    ,@('9135517','Mohammad',          'Al Masalma',          'WIC',  '')
    ,@('9126886','Negin',             'Bazmi',               'WIC',  '')
    ,@('9124144','Olaf',              'Wittenberg',          'WIC',  '')
    ,@('9133998',([char]0xD6+'nder'), 'Arslan',              'WIC',  '')   # Önder
    ,@('9120980','Rene',              'Altmeyer',            'WIC',  '')
    ,@('9074573','Sebastian',         ([char]0x48+[char]0xF6+'ck'), 'WIC','')  # Höck
    ,@('9132079','Sebastian',         'Lewandowski',         'WIC',  '')
    ,@('9129429','Senthuran',         'Shanmugalingam',      'WIC',  '')
    ,@('9124691','Sina',              'Sidharthan',          'WIC',  '')
    ,@('9074466','Stojnic',           'Nebojsa',             'WIC',  '')
    ,@('9117836','Suhrab',            'Sadieqy',             'WIC',  '')
    ,@('9125521','Tim',               'Boger',               'WIC',  '')
    ,@('9133995','Viktor',            'Winter',              'WIC',  '')
    ,@('9106138','Yun Hee',           'Oh',                  'WIC',  '')
    # VWIC (4) ────────────────────────────────────────────────────────────
    ,@('9120970','Amani',             'Kedo',                'VWIC', '')
    ,@('3193174','Anifa',             'Ngcongo',             'VWIC', '')
    ,@('3193177','Gunter',            'Dinkelmann',          'VWIC', '')
    ,@('3193175','Isloodien Hurchem', 'Lawrence',            'VWIC', '')
    # Voice (37) ──────────────────────────────────────────────────────────
    ,@('9074334','Aleksandrina',      'Dencheva',            'Voice','')
    ,@('9074341','Anas',              'Daba',                'Voice','')
    ,@('9090511','Annabela',          'Scavo',               'Voice','')
    ,@('9076905','Arevig',            'Ketenjian',           'Voice','')
    ,@('9074348','Asal Wardaastiani', 'Azar',                'Voice','')
    ,@('9124690','Boris',             'Kostov',              'Voice','')
    ,@('9074356','Christian',         'Koch',                'Voice','')
    ,@('9114617','Christian',         'Pastors',             'Voice','')
    ,@('9074363','Danny',             'Bendig',              'Voice','')
    ,@('9074364','Darjusch',          'Dropczinsky',         'Voice','')
    ,@('9074373','Duc Quy',           'Huynh',               'Voice','')
    ,@('9074375','Elena',             'Schlosser',           'Voice','')
    ,@('9132851','Elliot',            'van Staveren Kuste',  'Voice','')
    ,@('9074381','Eva-Liane',         'Schliwa',             'Voice','')
    ,@('9119463','Jonathan',          'Freudenthaler',       'Voice','')
    ,@('9133997','Kai Erik',          'Kumlehn',             'Voice','')
    ,@('9125526','Kemal',             'Sener',               'Voice','')
    ,@('9074528','Kolja',             'Christlieb',          'Voice','')
    ,@('9074535','Lubomir',           'Stoyanov',            'Voice','')
    ,@('9074543','Martijn',           'Brinks',              'Voice','')
    ,@('9087657','Meik',              ([char]0x53+[char]0x63+[char]0x68+[char]0xFC+'lgen'), 'Voice','') # Schülgen
    ,@('9132070','Mitchel',           'Sullivan',            'Voice','')
    ,@('9074549','Mitko',             'Kilogramski',         'Voice','')
    ,@('9114618','Mustafa',           'Deveci',              'Voice','')
    ,@('9074563','Ralf',              'Turski',              'Voice','')
    ,@('9074611','Rene Raoul',        'Aboikoni',            'Voice','')
    ,@('9090514','Sam Alisha',        'Metzner',             'Voice','')
    ,@('9086366','Tarek',             'Tabbara',             'Voice','')
    ,@('9092596','Timon',             'Philippen',           'Voice','')
    ,@('9074590','Tri Toan',          'Nguyen',              'Voice','')
    ,@('9124695','Veronika',          'Kouwui',              'Voice','')
    ,@('9086658','Vincent',           'Grunzel',             'Voice','')
    ,@('9074595','Virgil',            'Stelk',               'Voice','')
    ,@('9085121','Walter',            'Buxbaum',             'Voice','')
    ,@('9074428','Yevgeni',           'Frenkel',             'Voice','')
    ,@('9044352','Yolanda',           'Coppers',             'Voice','')
    ,@('9124687','Zehra Sila',        ([char]0x47+[char]0xF6+'rg'+[char]0xFC+'n'), 'Voice','') # Görgün
    # Other (24) — employee_id je NULL za E/P ID agente, oni idu kroz Korak 4
    ,@('9075030','Kevin',             'Haska',               'Other','Booking Tool')
    ,@('9074352','Burak',             'Kurtulmaz',           'Other','Bulk PWs')
    ,@('9085123','Anil',              'Bedzeti',             'Other','Chat')
    ,@('9133999','Marko',             'Bosnjak',             'Other','Chat')
    ,@('9074576','Sharon',            'Huber',               'Other','Chat')
    ,@('9126877','Ahmed',             'Hasanovic',           'Other','Chat CRO')
    ,@('9126887','Dominik',           'Bajic',               'Other','Chat CRO')
    ,@('9128158','Erne',              'Kis',                 'Other','Chat CRO')
    ,@('9074526','Kevin',             'Heynen',              'Other','Dispatcher')
    ,@('9090513','Michael',           'Holz',                'Other','Dispatcher')
    ,@('9085138','Christoph',         'Ulatowski',           'Other','SME')
    ,@('9074519','Jessica',           'Schlicht',            'Other','SME')
    ,@('9074559','Perim',             'Rollin',              'Other','SME')
    ,@('9074330','Adnan',             'Lelic',               'Other','SSP')
    ,@('9074345','Angelika',          'Weber',               'Other','SSP')
    ,@('9074350','Baschir',           'Mahrufi',             'Other','SSP')
    ,@('9074518','Javier',            'Sang',                'Other','SSP')
    ,@('9074557','Pascal',            'Dutz',                'Other','SSP')
    ,@('9074582','Stephan',           'Becker',              'Other','SSP')
    ,@('9078602','Tim',               'Nguyen',              'Other','SSP')
    ,@('9074592','Victoria',          'Scholz',              'Other','SSP')
    ,@('9083024','Ercan',             'Akdeniz',             'Other','Trainer')
)

# Agenti ciji je ID zapravo primary_kid (nemaju numericni employee_id u DB)
$SPECIAL = @(
    ,@('Elias',   'Erdem')
    ,@('Patrick', 'Henschel')
)

# Svi aktivni employee_id-jevi (za deaktivaciju onih kojih nema na listi)
$ACTIVE_IDS = ($AGENTS | ForEach-Object { $_[0] }) -join "','"

# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ShiftKiosk — Agent Sync  |  $DB @ $SERVER" -ForegroundColor Magenta
Write-Host "  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor DarkGray

# ── KORAK 1: Konekcija ───────────────────────────────────────────────────────
Write-Step 1 "Konekcija na SQL Server"

$conn = New-Object System.Data.SqlClient.SqlConnection $CONNSTR
try {
    $conn.Open()
    Write-OK "Spojeno: $SERVER / $DB"
} catch {
    Write-Err "Konekcija neuspjesna: $_"
    exit 1
}

# ── KORAK 2: Stanje PRIJE ────────────────────────────────────────────────────
Write-Step 2 "Stanje agents tabele PRIJE synca"

$before = Invoke-Sql $conn @'
SELECT
    ISNULL(NULLIF(location,''), '(prazno)') AS Kategorija,
    COUNT(*)                               AS Ukupno,
    SUM(CASE WHEN active=1 THEN 1 ELSE 0 END) AS Aktivni
FROM agents
GROUP BY ISNULL(NULLIF(location,''), '(prazno)')
ORDER BY Kategorija;
'@

foreach ($r in $before[0].Rows) {
    Write-Inf ("{0,-20}  ukupno={1}  aktivni={2}" -f $r.Kategorija, $r.Ukupno, $r.Aktivni)
}
$totalBefore = ($before[0].Rows | Measure-Object -Property Ukupno -Sum).Sum
Write-OK "Ukupno redova: $totalBefore"

# ── KORAK 3: MERGE po employee_id (118 agenata) ──────────────────────────────
Write-Step 3 "MERGE po employee_id (118 agenata sa numerickim ID)"
Write-Inf "MATCHED  → UPDATE first_name, last_name, active=1  (cuva team_leader i wic_location)"
Write-Inf "NOT MATCHED → INSERT sa location=kategorija, team_leader=kategorija"

$insertCount = 0
$updateCount = 0
$skipCount   = 0

foreach ($a in $AGENTS) {
    $empId    = $a[0]
    $first    = $a[1]
    $last     = $a[2]
    $category = $a[3]
    $eng      = $a[4]

    # Provjeri postoji li agent po employee_id
    $checkCmd = $conn.CreateCommand()
    $checkCmd.CommandText = "SELECT id, first_name, last_name FROM agents WHERE employee_id=@eid"
    $checkCmd.Parameters.AddWithValue('@eid', $empId) | Out-Null
    $reader = $checkCmd.ExecuteReader()
    $exists = $false
    $agentId = $null
    $oldFirst = ''; $oldLast = ''
    if ($reader.Read()) {
        $exists  = $true
        $agentId = $reader['id']
        $oldFirst = $reader['first_name']
        $oldLast  = $reader['last_name']
    }
    $reader.Close()

    if ($exists) {
        # UPDATE — samo first/last/active; ne diras team_leader ni wic_location
        $upd = $conn.CreateCommand()
        $upd.CommandText = @"
UPDATE agents
SET first_name=@fn, last_name=@ln, active=1
WHERE id=@id
"@
        $upd.Parameters.AddWithValue('@fn', $first) | Out-Null
        $upd.Parameters.AddWithValue('@ln', $last)  | Out-Null
        $upd.Parameters.AddWithValue('@id', $agentId) | Out-Null
        $upd.ExecuteNonQuery() | Out-Null

        $nameChanged = ($oldFirst -ne $first) -or ($oldLast -ne $last)
        if ($nameChanged) {
            Write-Inf ("  UPDATE ime: [{0}] {1} {2}  →  {3} {4}" -f $empId, $oldFirst, $oldLast, $first, $last)
        }
        $updateCount++
    } else {
        # INSERT — novi agent
        $ins = $conn.CreateCommand()
        $ins.CommandText = @"
INSERT INTO agents (first_name, last_name, employee_id, team_leader, location, engagement, active)
VALUES (@fn, @ln, @eid, @tl, @loc, @eng, 1)
"@
        $ins.Parameters.AddWithValue('@fn',  $first)    | Out-Null
        $ins.Parameters.AddWithValue('@ln',  $last)     | Out-Null
        $ins.Parameters.AddWithValue('@eid', $empId)    | Out-Null
        $ins.Parameters.AddWithValue('@tl',  $category) | Out-Null
        $ins.Parameters.AddWithValue('@loc', $category) | Out-Null
        $ins.Parameters.AddWithValue('@eng', $eng)      | Out-Null
        $ins.ExecuteNonQuery() | Out-Null

        Write-Inf ("  INSERT: [{0}] {1} {2}  [{3}]{4}" -f $empId, $first, $last, $category, $(if($eng){" / $eng"}))
        $insertCount++
    }
}

Write-OK "MERGE zavrsen: UPDATE=$updateCount  INSERT=$insertCount"

# ── KORAK 4: Specijalni agenti (E26615, P37233) — match po full_name ─────────
Write-Step 4 "Specijalni agenti bez numerickog employee_id (Elias Erdem, Patrick Henschel)"
Write-Inf "Matchuje po first_name + last_name. Samo active=1, ime vec ispravno."

foreach ($s in $SPECIAL) {
    $fn = $s[0]; $ln = $s[1]
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "UPDATE agents SET active=1 WHERE first_name=@fn AND last_name=@ln"
    $cmd.Parameters.AddWithValue('@fn', $fn) | Out-Null
    $cmd.Parameters.AddWithValue('@ln', $ln) | Out-Null
    $rows = $cmd.ExecuteNonQuery()
    if ($rows -gt 0) {
        Write-OK "  Pronadjen i aktiviran: $fn $ln"
    } else {
        Write-Warn "  NIJE PRONADJEN: $fn $ln  (treba rucni INSERT)"
    }
}

# ── KORAK 5: VWIC team_leader ────────────────────────────────────────────────
Write-Step 5 "VWIC distinkcija — postavi team_leader='VWIC' za 4 VWIC agenta"
Write-Inf "Ovo ih odvaja u TL filteru na dashboardu."

$vwicIds = @('9120970','3193174','3193177','3193175')
foreach ($vid in $vwicIds) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "UPDATE agents SET team_leader='VWIC', location='VWIC' WHERE employee_id=@eid"
    $cmd.Parameters.AddWithValue('@eid', $vid) | Out-Null
    $rows = $cmd.ExecuteNonQuery()
    Write-Inf ("  employee_id={0}  →  rows affected={1}" -f $vid, $rows)
}
Write-OK "VWIC team_leader/location postavljen"

# ── KORAK 6: Deaktivacija agenata koji nisu na listi ─────────────────────────
Write-Step 6 "Deaktivacija agenata koji NISU na aktuelnoj listi"
Write-Inf "Ovo ne brise redove — samo active=0. Iskljucuje E/P agente iz deaktivacije."

# Svi employee_id koji ostaju aktivni (118 numerickih)
$idList = ($AGENTS | ForEach-Object { "'$($_[0])'" }) -join ','

$deactivateSql = @"
UPDATE agents
SET active = 0
WHERE active = 1
  AND employee_id IS NOT NULL
  AND employee_id != ''
  AND employee_id NOT IN ($idList);
"@

$deactivated = Invoke-NonQuery $conn $deactivateSql
if ($deactivated -gt 0) {
    Write-Warn "Deaktivirano $deactivated agenata koji nisu na aktuelnoj listi"

    # Pokazi koji su deaktivirani
    $showDeact = Invoke-Sql $conn @"
SELECT employee_id, first_name + ' ' + last_name AS ime, team_leader
FROM agents
WHERE active = 0
  AND employee_id IS NOT NULL
  AND employee_id NOT IN ($idList)
ORDER BY last_name;
"@
    foreach ($r in $showDeact[0].Rows) {
        Write-Inf ("  deaktiviran: [{0}] {1}  (TL: {2})" -f $r.employee_id, $r.ime, $r.team_leader)
    }
} else {
    Write-OK "Nema agenata za deaktivaciju"
}

# ── KORAK 7: Stanje POSLIJE + VWIC provjera ──────────────────────────────────
Write-Step 7 "Stanje POSLIJE synca + VWIC provjera"

$after = Invoke-Sql $conn @'
SELECT
    ISNULL(NULLIF(team_leader,''), ISNULL(NULLIF(location,''),'(ostalo)')) AS Grupa,
    COUNT(*) AS Ukupno
FROM agents
WHERE active = 1
GROUP BY ISNULL(NULLIF(team_leader,''), ISNULL(NULLIF(location,''),'(ostalo)'))
ORDER BY Ukupno DESC;
'@

Write-Host ""
Write-Host "  Aktivni agenti po grupi (team_leader / location):" -ForegroundColor White
foreach ($r in $after[0].Rows) {
    $col = if ($r.Grupa -eq 'VWIC') { 'Cyan' } else { 'Gray' }
    Write-Host ("  {0,-30}  {1}" -f $r.Grupa, $r.Ukupno) -ForegroundColor $col
}

# VWIC eksplicitna lista
Write-Host ""
Write-Host "  VWIC agenti (provjera):" -ForegroundColor Cyan
$vwic = Invoke-Sql $conn @'
SELECT employee_id, first_name + ' ' + last_name AS ime, windows_username, active
FROM agents
WHERE location = 'VWIC' OR team_leader = 'VWIC'
ORDER BY last_name;
'@
if ($vwic[0].Rows.Count -eq 0) {
    Write-Warn "  Nema VWIC agenata!"
} else {
    foreach ($r in $vwic[0].Rows) {
        $u = if ([string]::IsNullOrWhiteSpace($r.windows_username)) { '(bez username)' } else { $r.windows_username }
        Write-Host ("  [{0}]  {1,-30}  {2}" -f $r.employee_id, $r.ime, $u) -ForegroundColor Cyan
    }
}

# Ukupan broj aktivnih
$totalAfter = (Invoke-Sql $conn "SELECT COUNT(*) AS n FROM agents WHERE active=1")[0].Rows[0].n

$conn.Close()

Write-Host ""
Write-Host "  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
Write-Host ("  Sync kompletiran  |  INSERT={0}  UPDATE={1}  Deakt={2}  Aktivnih={3}" `
    -f $insertCount, $updateCount, $deactivated, $totalAfter) -ForegroundColor Magenta
Write-Host "  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
Write-Host ""
