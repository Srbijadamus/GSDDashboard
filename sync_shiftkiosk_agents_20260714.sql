-- ============================================================
-- ShiftKiosk agents sync -- 2026-07-14
-- Source: PS1_71_ExportActiveAgents.ps1 (120 active agents)
-- DB: ShiftKioskDB @ localhost\SQLEXPRESS
--
-- WHEN MATCHED    → update first_name, last_name, active=1
--                   (preserves existing team_leader, windows_username)
-- WHEN NOT MATCHED → insert with location = kategorie (WIC/VWIC/Voice/Other)
--                    engagement = subrolle fuer Other-agents
--
-- VWIC-agents: location='VWIC', team_leader='VWIC'
-- Optional am ende: deaktiviere agents die nicht mehr auf der liste stehen
-- ============================================================

USE ShiftKioskDB;
GO

MERGE INTO agents AS tgt
USING (VALUES

    -- ── WIC (55) ──────────────────────────────────────────────
    ('9130648', 'Aakash',              'Som',                    'WIC',   ''),
    ('9125519', 'Abdulrahman',         'Aldera',                 'WIC',   ''),
    ('9126881', 'Adam',                'Szilvagyi',              'WIC',   ''),
    ('9129441', 'Ahmad',               'Dabbas',                 'WIC',   ''),
    ('9120965', 'Amir',                'Nassri',                 'WIC',   ''),
    ('9124145', 'Anisha Nellikka',     'Panikkan',               'WIC',   ''),
    ('9047339', 'Ayten',               'Karatas',                'WIC',   ''),
    ('9129428', 'Binod',               'Dutta',                  'WIC',   ''),
    ('9117834', 'Bishal',              'Maharjan',               'WIC',   ''),
    ('9124688', 'Christian',           'Martino',                'WIC',   ''),
    ('9107615', 'Dennis',              'Markus',                 'WIC',   ''),
    ('9130657', 'Dmytro',              'Shelikhov',              'WIC',   ''),
    ('9126880', 'Elaheh',              'Ramzi',                  'WIC',   ''),
    ('9122676', 'Erdal',               'Coskun',                 'WIC',   ''),
    ('9074431', 'Erik',                'Goecks',                 'WIC',   ''),
    ('9128148', 'Eyup',                'Akyurek',                'WIC',   ''),
    ('9132075', 'Felix',               'Spindler',               'WIC',   ''),
    ('9128153', 'Francois',            'Sicot',                  'WIC',   ''),
    ('9122679', 'Hamyaz',              'Pathan',                 'WIC',   ''),
    ('9130650', 'Hamza',               'Forrousso',              'WIC',   ''),
    ('9112563', 'Hesham',              'Montasser',              'WIC',   ''),
    ('9122674', 'Holger',              'Kuhlmann',               'WIC',   ''),
    ('9125517', 'Holger',              'Petzholdt',              'WIC',   ''),
    ('9126883', 'Ion',                 'Bodnariuc',              'WIC',   ''),
    ('9074512', 'Ivan',                'Leurs',                  'WIC',   ''),
    ('9126874', 'Jannik',              'Borner',                 'WIC',   ''),
    ('9125516', 'Joel',                'Broring',                'WIC',   ''),
    ('9129427', 'John Daniel',         'Wendland',               'WIC',   ''),
    ('9120971', 'Kaan',                'Arslan',                 'WIC',   ''),
    ('9112561', 'Kamil',               'Filipowicz',             'WIC',   ''),
    ('9126882', 'Khaled',              'Alali',                  'WIC',   ''),
    ('9107616', 'Klaus',               'Friedrich',              'WIC',   ''),
    ('9084156', 'Krishnendu',          'Das',                    'WIC',   ''),
    ('9130643', 'Lukas',               'Schiefele',              'WIC',   ''),
    ('9121951', 'Mahboubeh',           'Abdighara',              'WIC',   ''),
    ('9130649', 'Marcus',              'Rusch',                  'WIC',   ''),
    ('9128157', 'Mariusz',             'Kozinski',               'WIC',   ''),
    ('9122675', 'Mark',                'Bachmann',               'WIC',   ''),
    ('9135516', 'Mehmet',              'Tigli',                  'WIC',   ''),
    ('9124697', 'Merlin',              'Voss',                   'WIC',   ''),
    ('9132077', 'Mohamad Nasir',       'Amany',                  'WIC',   ''),
    ('9135517', 'Mohammad',            'Al Masalma',             'WIC',   ''),
    ('9126886', 'Negin',               'Bazmi',                  'WIC',   ''),
    ('9124144', 'Olaf',                'Wittenberg',             'WIC',   ''),
    ('9133998', N'Önder',              'Arslan',                 'WIC',   ''),
    ('9120980', 'Rene',                'Altmeyer',               'WIC',   ''),
    ('9074573', N'Sebastian',          N'Höck',                  'WIC',   ''),
    ('9132079', 'Sebastian',           'Lewandowski',            'WIC',   ''),
    ('9129429', 'Senthuran',           'Shanmugalingam',         'WIC',   ''),
    ('9124691', 'Sina',                'Sidharthan',             'WIC',   ''),
    ('9074466', 'Stojnic',             'Nebojsa',                'WIC',   ''),
    ('9117836', 'Suhrab',              'Sadieqy',                'WIC',   ''),
    ('9125521', 'Tim',                 'Boger',                  'WIC',   ''),
    ('9133995', 'Viktor',              'Winter',                 'WIC',   ''),
    ('9106138', 'Yun Hee',             'Oh',                     'WIC',   ''),

    -- ── VWIC (4) ─────────────────────────────────────────────
    ('9120970', 'Amani',               'Kedo',                   'VWIC',  ''),
    ('3193174', 'Anifa',               'Ngcongo',                'VWIC',  ''),
    ('3193177', 'Gunter',              'Dinkelmann',             'VWIC',  ''),
    ('3193175', 'Isloodien Hurchem',   'Lawrence',               'VWIC',  ''),

    -- ── Voice (37) ───────────────────────────────────────────
    ('9074334', 'Aleksandrina',        'Dencheva',               'Voice', ''),
    ('9074341', 'Anas',                'Daba',                   'Voice', ''),
    ('9090511', 'Annabela',            'Scavo',                  'Voice', ''),
    ('9076905', 'Arevig',              'Ketenjian',              'Voice', ''),
    ('9074348', 'Asal Wardaastiani',   'Azar',                   'Voice', ''),
    ('9124690', 'Boris',               'Kostov',                 'Voice', ''),
    ('9074356', 'Christian',           'Koch',                   'Voice', ''),
    ('9114617', 'Christian',           'Pastors',                'Voice', ''),
    ('9074363', 'Danny',               'Bendig',                 'Voice', ''),
    ('9074364', 'Darjusch',            'Dropczinsky',            'Voice', ''),
    ('9074373', 'Duc Quy',             'Huynh',                  'Voice', ''),
    ('9074375', 'Elena',               'Schlosser',              'Voice', ''),
    ('9132851', 'Elliot',              'van Staveren Kuste',     'Voice', ''),
    ('9074381', 'Eva-Liane',           'Schliwa',                'Voice', ''),
    ('9119463', 'Jonathan',            'Freudenthaler',          'Voice', ''),
    ('9133997', 'Kai Erik',            'Kumlehn',                'Voice', ''),
    ('9125526', 'Kemal',               'Sener',                  'Voice', ''),
    ('9074528', 'Kolja',               'Christlieb',             'Voice', ''),
    ('9074535', 'Lubomir',             'Stoyanov',               'Voice', ''),
    ('9074543', 'Martijn',             'Brinks',                 'Voice', ''),
    ('9087657', N'Meik',               N'Schülgen',              'Voice', ''),
    ('9132070', 'Mitchel',             'Sullivan',               'Voice', ''),
    ('9074549', 'Mitko',               'Kilogramski',            'Voice', ''),
    ('9114618', 'Mustafa',             'Deveci',                 'Voice', ''),
    ('9074563', 'Ralf',                'Turski',                 'Voice', ''),
    ('9074611', 'Rene Raoul',          'Aboikoni',               'Voice', ''),
    ('9090514', 'Sam Alisha',          'Metzner',                'Voice', ''),
    ('9086366', 'Tarek',               'Tabbara',                'Voice', ''),
    ('9092596', 'Timon',               'Philippen',              'Voice', ''),
    ('9074590', 'Tri Toan',            'Nguyen',                 'Voice', ''),
    ('9124695', 'Veronika',            'Kouwui',                 'Voice', ''),
    ('9086658', 'Vincent',             'Grunzel',                'Voice', ''),
    ('9074595', 'Virgil',              'Stelk',                  'Voice', ''),
    ('9085121', 'Walter',              'Buxbaum',                'Voice', ''),
    ('9074428', 'Yevgeni',             'Frenkel',                'Voice', ''),
    ('9044352', 'Yolanda',             'Coppers',                'Voice', ''),
    ('9124687', 'Zehra Sila',          N'Görgün',                'Voice', ''),

    -- ── Other (24) ───────────────────────────────────────────
    ('E26615',  'Elias',               'Erdem',                  'Other', ''),
    ('P37233',  'Patrick',             'Henschel',               'Other', ''),
    ('9075030', 'Kevin',               'Haska',                  'Other', 'Booking Tool'),
    ('9074352', 'Burak',               'Kurtulmaz',              'Other', 'Bulk PWs'),
    ('9085123', 'Anil',                'Bedzeti',                'Other', 'Chat'),
    ('9133999', 'Marko',               'Bosnjak',                'Other', 'Chat'),
    ('9074576', 'Sharon',              'Huber',                  'Other', 'Chat'),
    ('9126877', 'Ahmed',               'Hasanovic',              'Other', 'Chat CRO'),
    ('9126887', 'Dominik',             'Bajic',                  'Other', 'Chat CRO'),
    ('9128158', 'Erne',                'Kis',                    'Other', 'Chat CRO'),
    ('9074526', 'Kevin',               'Heynen',                 'Other', 'Dispatcher'),
    ('9090513', 'Michael',             'Holz',                   'Other', 'Dispatcher'),
    ('9085138', 'Christoph',           'Ulatowski',              'Other', 'SME'),
    ('9074519', 'Jessica',             'Schlicht',               'Other', 'SME'),
    ('9074559', 'Perim',               'Rollin',                 'Other', 'SME'),
    ('9074330', 'Adnan',               'Lelic',                  'Other', 'SSP'),
    ('9074345', 'Angelika',            'Weber',                  'Other', 'SSP'),
    ('9074350', 'Baschir',             'Mahrufi',                'Other', 'SSP'),
    ('9074518', 'Javier',              'Sang',                   'Other', 'SSP'),
    ('9074557', 'Pascal',              'Dutz',                   'Other', 'SSP'),
    ('9074582', 'Stephan',             'Becker',                 'Other', 'SSP'),
    ('9078602', 'Tim',                 'Nguyen',                 'Other', 'SSP'),
    ('9074592', 'Victoria',            'Scholz',                 'Other', 'SSP'),
    ('9083024', 'Ercan',               'Akdeniz',                'Other', 'Trainer')

) AS src(employee_id, first_name, last_name, location, engagement)
ON tgt.employee_id = src.employee_id

WHEN MATCHED THEN
    UPDATE SET
        tgt.first_name = src.first_name,
        tgt.last_name  = src.last_name,
        tgt.active     = 1
        -- location i team_leader se ne diraju za postojece agente
        -- da se sacuvaju pravi WIC lokacioni podaci

WHEN NOT MATCHED BY TARGET THEN
    INSERT (first_name, last_name, employee_id, location, engagement, team_leader, active)
    VALUES (src.first_name, src.last_name, src.employee_id,
            src.location, src.engagement,
            src.location,   -- team_leader = location-category za nove agente
            1);

-- ── Opciono: deaktiviraj agente koji nisu vise na listi ──────
-- WHEN NOT MATCHED BY SOURCE THEN
--     UPDATE SET tgt.active = 0;
-- (odkomentiraj samo ako si siguran — deaktivira SVE koji nisu u gornjoj listi)
-- ────────────────────────────────────────────────────────────

GO

-- Provjera rezultata
SELECT
    location,
    COUNT(*) AS ukupno,
    STRING_AGG(first_name + ' ' + last_name, ', ')
        WITHIN GROUP (ORDER BY last_name) AS agenti
FROM agents
WHERE active = 1
GROUP BY location
ORDER BY location;

GO

-- Detaljni pregled po kategoriji
SELECT
    CASE location
        WHEN 'WIC'   THEN '1-WIC'
        WHEN 'VWIC'  THEN '2-VWIC'
        WHEN 'Voice' THEN '3-Voice'
        WHEN 'Other' THEN '4-Other'
        ELSE               '5-Ostalo'
    END AS kategorija,
    employee_id,
    first_name + ' ' + last_name AS full_name,
    engagement
FROM agents
WHERE active = 1
ORDER BY kategorija, last_name, first_name;
GO
