USE GSDDashboard;

-- ── Schema additions ──────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'WicLocations') AND name = N'IsNpp')
    ALTER TABLE WicLocations ADD IsNpp BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'Employees') AND name = N'NppQualified')
    ALTER TABLE Employees ADD NppQualified BIT NOT NULL DEFAULT 0;

-- ── Mark NPP sites ─────────────────────────────────────────────────────────────
-- Emmerthal, Essenbach, Brokdorf, Stade, Stadland, Grafenrheinfeld
-- Wesel is NOT NPP. Hannover is excluded entirely.

-- DB uses DE~PostalCode~City~Address format codes; match by DisplayName which is stable
UPDATE WicLocations
SET IsNpp = 1
WHERE DisplayName IN (
    'Emmerthal',
    'Essenbach',
    'Brokdorf',
    'Stade',
    'Stadland',
    'Grafenrheinfeld'
);

-- ── Mark NPP-qualified agents ─────────────────────────────────────────────────
-- Abdulrahman Aldera (9125519), Adam Szilvagyi (9126881), Angelika Weber (9074345),
-- Holger Petzholdt (9125517), Jannik Borner (9126874), Joel Broring (9125516),
-- Lukas Schiefele (9130643), Merlin Voss (9124697), Olaf Wittenberg (9124144),
-- Tim Boger (9125521), Viktor Winter (9133995),
-- Ercan Akdeniz (9083024) — NPP-qualified only; he has no home WIC; do not add WIC assignments.

UPDATE Employees
SET NppQualified = 1
WHERE EmployeeId IN (
    '9125519',  -- Abdulrahman Aldera
    '9126881',  -- Adam Szilvagyi
    '9074345',  -- Angelika Weber
    '9125517',  -- Holger Petzholdt
    '9126874',  -- Jannik Borner
    '9125516',  -- Joel Broring
    '9130643',  -- Lukas Schiefele
    '9124697',  -- Merlin Voss
    '9124144',  -- Olaf Wittenberg
    '9125521',  -- Tim Boger
    '9133995',  -- Viktor Winter
    '9083024'   -- Ercan Akdeniz (qualification only; no home WIC added)
);

-- ── Verification queries (run manually to confirm) ────────────────────────────
-- SELECT LocationCode, DisplayName, IsNpp FROM WicLocations WHERE IsNpp = 1;
-- SELECT LocationCode, DisplayName, IsNpp FROM WicLocations WHERE LocationCode IN ('DE_Wesel','DE_Hannover');
-- SELECT EmployeeId, FullName, NppQualified FROM Employees WHERE NppQualified = 1;
