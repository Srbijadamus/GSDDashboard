-- WIC Plan Update (idempotent, ASCII-only, GO-separated for real line numbers)
SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== WIC PLAN UPDATE starting ===';
BEGIN TRAN;
PRINT N'Transaction started.';
GO

-- ============================================================
-- PART 1: WicLocations OpeningDay + Comment
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: WicLocations fields...';

UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Hamburg';
UPDATE WicLocations SET Comment = N'Lukas live in Hannova'
    WHERE DisplayName = N'Hannover';
UPDATE WicLocations SET Comment = N'check with Hannover together, the backup can''t be used, need 1 more'
    WHERE DisplayName = N'Emmerthal';
UPDATE WicLocations SET Comment = N'combine with Landshut, maybe need one more backup'
    WHERE DisplayName = N'M' + NCHAR(252) + N'nchen';
UPDATE WicLocations SET Comment = N'combine with M' + NCHAR(252) + N'nchen, maybe need one more backup'
    WHERE DisplayName = N'Landshut';
UPDATE WicLocations SET Comment = N'no backup'
    WHERE DisplayName = N'Essenbach';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Augsburg';
UPDATE WicLocations SET Comment = N'Demmin 2 WICs can be combined together'
    WHERE DisplayName = N'Demmin - Woldeforster Str';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Demmin - Am Hanseufer';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Rendsburg';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Helmstedt';
UPDATE WicLocations SET OpeningDay = N'daily 3', Comment = N''
    WHERE DisplayName = N'Essen - BP1';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Essen - TK';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'M' + NCHAR(252) + N'lheim';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Salzgitter';
UPDATE WicLocations SET Comment = N'Hamyaz backup for several WIC, maybe need 1 more backup'
    WHERE DisplayName = N'F' + NCHAR(252) + N'rstenwalde';
UPDATE WicLocations SET Comment = N'Hamyaz backup for several WIC, maybe need 1 more backup'
    WHERE DisplayName = N'Potsdam';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Quickborn';
UPDATE WicLocations SET Comment = N'Aman Kedo supported as backup before'
    WHERE DisplayName = N'Arnsberg';
UPDATE WicLocations SET Comment = N'Amani as Main agent in discussing'
    WHERE DisplayName = N'Dortmund';
UPDATE WicLocations SET Comment = N'with car'
    WHERE DisplayName = N'M' + NCHAR(252) + N'nster';
UPDATE WicLocations SET Comment = N'with car'
    WHERE DisplayName = N'Osnabr' + NCHAR(252) + N'ck';
UPDATE WicLocations SET Comment = N'with car'
    WHERE DisplayName = N'Recklinghausen';
UPDATE WicLocations SET Comment = N'with car'
    WHERE DisplayName = N'Wesel';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Neuss';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Neu-Isenburg';
UPDATE WicLocations SET Comment = N'Viktor supported 1 time before, but maybe need a new backup.'
    WHERE DisplayName = N'Brokdorf';
UPDATE WicLocations SET Comment = N'no backup available, because of the demands from Stadland'
    WHERE DisplayName = N'Stade';
UPDATE WicLocations SET Comment = N'no backup available on Mo.and Do.'
    WHERE DisplayName = N'Stadland';
UPDATE WicLocations SET Comment = N'no backup now, and on Oct and Nov Tim will have a 3 weeks leave'
    WHERE DisplayName = N'Grafenrheinfeld';
UPDATE WicLocations SET Comment = N'Berlin 2 WICs can be combined together'
    WHERE DisplayName = N'Berlin - Gau' + NCHAR(223) + N'str.';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Berlin - Br' + NCHAR(252) + N'ckenstrasse';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Saffig';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Siegen';
UPDATE WicLocations SET Comment = N'no backup on Do. Prio. higher than Trier'
    WHERE DisplayName = N'Saarbr' + NCHAR(252) + N'cken';
UPDATE WicLocations SET Comment = N'no backup now'
    WHERE DisplayName = N'Trier';
UPDATE WicLocations SET Comment = N'new additional person there, rollout ends Nov'
    WHERE DisplayName = N'Halle';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Markkleeberg';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Bamberg';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N's-Hertogenbosch';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Zwolle';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Pfaffenhofen';
UPDATE WicLocations SET Comment = N''
    WHERE DisplayName = N'Regensburg';

PRINT N'Part 1 done.';
GO

-- ============================================================
-- PART 2: WicAgentAssignments DEACTIVATE stale rows
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Deactivating stale assignments...';

-- Hamburg: Elias Erdem BACKUP -> will become REGIONAL
UPDATE waa SET IsActive = 0
FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName = N'Hamburg' AND waa.AssignmentType = N'BACKUP'
  AND waa.EmployeeName = N'Elias Erdem' AND waa.IsActive = 1;

-- Hannover: Lukas Schiefele REGIONAL -> will become BACKUP
UPDATE waa SET IsActive = 0
FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName = N'Hannover' AND waa.AssignmentType = N'REGIONAL'
  AND waa.EmployeeName = N'Lukas Schiefele' AND waa.IsActive = 1;

-- Landshut: remove Eyup Akyurek, Adam Szilvagyi, Sina Sidharthan from BACKUP
UPDATE waa SET IsActive = 0
FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName = N'Landshut' AND waa.AssignmentType = N'BACKUP'
  AND waa.EmployeeName IN (N'Eyup Akyurek', N'Adam Szilvagyi', N'Sina Sidharthan')
  AND waa.IsActive = 1;

-- Augsburg: remove Sina Sidharthan from BACKUP
UPDATE waa SET IsActive = 0
FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName = N'Augsburg' AND waa.AssignmentType = N'BACKUP'
  AND waa.EmployeeName = N'Sina Sidharthan' AND waa.IsActive = 1;

-- Demmin - Woldeforster Str: Sebastian Lewandowski, Hamyaz Pathan, Viktor Winter BACKUP -> REGIONAL
UPDATE waa SET IsActive = 0
FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName = N'Demmin - Woldeforster Str' AND waa.AssignmentType = N'BACKUP'
  AND waa.EmployeeName IN (N'Sebastian Lewandowski', N'Hamyaz Pathan', N'Viktor Winter')
  AND waa.IsActive = 1;

-- Demmin - Am Hanseufer: same
UPDATE waa SET IsActive = 0
FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName = N'Demmin - Am Hanseufer' AND waa.AssignmentType = N'BACKUP'
  AND waa.EmployeeName IN (N'Sebastian Lewandowski', N'Hamyaz Pathan', N'Viktor Winter')
  AND waa.IsActive = 1;

-- Salzgitter: Aakash Som MAIN -> will become BACKUP
UPDATE waa SET IsActive = 0
FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName = N'Salzgitter' AND waa.AssignmentType = N'MAIN'
  AND waa.EmployeeName = N'Aakash Som' AND waa.IsActive = 1;

-- Neu-Isenburg: Mohammad Al Masalama MAIN -> will become BACKUP
UPDATE waa SET IsActive = 0
FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName = N'Neu-Isenburg' AND waa.AssignmentType = N'MAIN'
  AND waa.EmployeeName = N'Mohammad Al Masalama' AND waa.IsActive = 1;

-- Pfaffenhofen: remove Christos Kyrillidis from BACKUP
UPDATE waa SET IsActive = 0
FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName = N'Pfaffenhofen' AND waa.AssignmentType = N'BACKUP'
  AND waa.EmployeeName = N'Christos Kyrillidis' AND waa.IsActive = 1;

PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: WicAgentAssignments INSERT new active rows
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: Inserting new assignments...';

-- Hamburg: REGIONAL Viktor Winter, Elias Erdem
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Viktor Winter', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Hamburg'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Viktor Winter' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Elias Erdem', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Hamburg'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Elias Erdem' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Hannover: BACKUP Lukas Schiefele; REGIONAL Aakash Som, Merlin Voss
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Lukas Schiefele', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Hannover'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Lukas Schiefele' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Aakash Som', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Hannover'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Aakash Som' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Merlin Voss', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Hannover'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Merlin Voss' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Munchen: REGIONAL Sina Sidharthan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Sina Sidharthan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'M' + NCHAR(252) + N'nchen'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Sina Sidharthan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Landshut: REGIONAL Sina Sidharthan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Sina Sidharthan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Landshut'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Sina Sidharthan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Essenbach: BACKUP Angelika Weber, Holger Petzholdt; REGIONAL Lukas Schiefele
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Angelika Weber', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Essenbach'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Angelika Weber' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Holger Petzholdt', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Essenbach'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Holger Petzholdt' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Lukas Schiefele', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Essenbach'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Lukas Schiefele' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Augsburg: REGIONAL Khaled Alali
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Khaled Alali', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Augsburg'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Khaled Alali' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Demmin - Woldeforster Str: REGIONAL Viktor Winter, Hamyaz Pathan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Viktor Winter', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Demmin - Woldeforster Str'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Viktor Winter' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Hamyaz Pathan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Demmin - Woldeforster Str'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Hamyaz Pathan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Demmin - Am Hanseufer: REGIONAL Viktor Winter, Hamyaz Pathan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Viktor Winter', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Demmin - Am Hanseufer'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Viktor Winter' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Hamyaz Pathan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Demmin - Am Hanseufer'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Hamyaz Pathan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Rendsburg: REGIONAL Bishal Maharjan, Amir Nassri
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Bishal Maharjan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Rendsburg'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Bishal Maharjan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Amir Nassri', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Rendsburg'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Amir Nassri' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Helmstedt: REGIONAL Lukas Schiefele
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Lukas Schiefele', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Helmstedt'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Lukas Schiefele' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Essen - BP1: REGIONAL Amani Kedo
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Amani Kedo', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Essen - BP1'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Amani Kedo' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Essen - TK: REGIONAL Patrick Henschel, Amani Kedo
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Patrick Henschel', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Essen - TK'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Patrick Henschel' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Amani Kedo', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Essen - TK'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Amani Kedo' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Salzgitter: BACKUP Aakash Som, Merlin Voss; REGIONAL Lukas Schiefele
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Aakash Som', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Salzgitter'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Aakash Som' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Merlin Voss', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Salzgitter'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Merlin Voss' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Lukas Schiefele', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Salzgitter'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Lukas Schiefele' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Furstenwalde: REGIONAL Krishnendu Das
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Krishnendu Das', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'F' + NCHAR(252) + N'rstenwalde'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Krishnendu Das' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Potsdam: REGIONAL Krishnendu Das
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Krishnendu Das', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Potsdam'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Krishnendu Das' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Quickborn: BACKUP Bishal Maharjan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Bishal Maharjan', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Quickborn'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Bishal Maharjan' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

-- Dortmund: REGIONAL Patrick Henschel, Amani Kedo
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Patrick Henschel', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Dortmund'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Patrick Henschel' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Amani Kedo', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Dortmund'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Amani Kedo' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Brokdorf: REGIONAL Angelika Weber
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Angelika Weber', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Brokdorf'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Angelika Weber' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Stade: BACKUP Angelika Weber
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Angelika Weber', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Stade'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Angelika Weber' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

-- Stadland: BACKUP Angelika Weber
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Angelika Weber', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Stadland'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Angelika Weber' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

-- Grafenrheinfeld: BACKUP Angelika Weber, Holger Petzholdt
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Angelika Weber', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Grafenrheinfeld'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Angelika Weber' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Holger Petzholdt', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Grafenrheinfeld'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Holger Petzholdt' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

-- Berlin - Gausstr.: REGIONAL Hamyaz Pathan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Hamyaz Pathan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Berlin - Gau' + NCHAR(223) + N'str.'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Hamyaz Pathan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Berlin - Bruckenstrasse: REGIONAL Hamyaz Pathan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Hamyaz Pathan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Berlin - Br' + NCHAR(252) + N'ckenstrasse'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Hamyaz Pathan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Siegen: BACKUP Mohammad Al Masalama
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Mohammad Al Masalama', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Siegen'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Mohammad Al Masalama' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

-- Saarbrucken: REGIONAL Negin Bazmi
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Negin Bazmi', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Saarbr' + NCHAR(252) + N'cken'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Negin Bazmi' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Trier: BACKUP Negin Bazmi (source had trailing ? - applied as UNCERTAIN)
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Negin Bazmi', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Trier'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Negin Bazmi' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

-- Halle: BACKUP Michael Holz; REGIONAL Hamyaz Pathan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Michael Holz', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Halle'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Michael Holz' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Hamyaz Pathan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Halle'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Hamyaz Pathan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Markkleeberg: REGIONAL Hamyaz Pathan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Hamyaz Pathan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Markkleeberg'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Hamyaz Pathan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Bamberg: REGIONAL Sina Sidharthan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Sina Sidharthan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Bamberg'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Sina Sidharthan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Neu-Isenburg: BACKUP Mohammad Al Masalama
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Mohammad Al Masalama', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Neu-Isenburg'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Mohammad Al Masalama' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

-- Pfaffenhofen: BACKUP Dmytro Shelikhov; REGIONAL Sina Sidharthan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Dmytro Shelikhov', N'BACKUP', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Pfaffenhofen'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Dmytro Shelikhov' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Sina Sidharthan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Pfaffenhofen'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Sina Sidharthan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

-- Regensburg: REGIONAL Sina Sidharthan
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
SELECT wl.LocationCode, N'Sina Sidharthan', N'REGIONAL', 1 FROM WicLocations wl
WHERE wl.DisplayName = N'Regensburg'
  AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Sina Sidharthan' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

PRINT N'Part 3 done.';
GO

-- ============================================================
-- PART 4: Conditional blocks (employee existence checks)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 4: Conditional employee checks...';

-- Jonathan Freudenthaler (Arnsberg MAIN + Mulheim BACKUP)
IF EXISTS (SELECT 1 FROM Employees WHERE FullName = N'Jonathan Freudenthaler')
BEGIN
    -- Arnsberg: deactivate Angelika Weber MAIN
    UPDATE waa SET IsActive = 0
    FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
    WHERE wl.DisplayName = N'Arnsberg' AND waa.AssignmentType = N'MAIN'
      AND waa.EmployeeName = N'Angelika Weber' AND waa.IsActive = 1;

    -- Arnsberg: add Jonathan Freudenthaler MAIN
    INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
    SELECT wl.LocationCode, N'Jonathan Freudenthaler', N'MAIN', 1 FROM WicLocations wl
    WHERE wl.DisplayName = N'Arnsberg'
      AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Jonathan Freudenthaler' AND e.AssignmentType = N'MAIN' AND e.IsActive = 1);

    -- Arnsberg: add Angelika Weber REGIONAL
    INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
    SELECT wl.LocationCode, N'Angelika Weber', N'REGIONAL', 1 FROM WicLocations wl
    WHERE wl.DisplayName = N'Arnsberg'
      AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Angelika Weber' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

    -- Mulheim: add Jonathan Freudenthaler BACKUP
    INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
    SELECT wl.LocationCode, N'Jonathan Freudenthaler', N'BACKUP', 1 FROM WicLocations wl
    WHERE wl.DisplayName = N'M' + NCHAR(252) + N'lheim'
      AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Jonathan Freudenthaler' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

    PRINT N'Jonathan Freudenthaler: FOUND - Arnsberg MAIN swapped, Angelika Weber -> REGIONAL, M' + NCHAR(252) + N'lheim BACKUP added.';
END
ELSE
BEGIN
    PRINT N'PENDING - Jonathan Freudenthaler not in Employees. Arnsberg keeps Angelika Weber as MAIN. M' + NCHAR(252) + N'lheim BACKUP left empty.';
END

-- Danny Bendig (Essen - BP1 + Essen - TK REGIONAL)
IF EXISTS (SELECT 1 FROM Employees WHERE FullName = N'Danny Bendig')
BEGIN
    INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
    SELECT wl.LocationCode, N'Danny Bendig', N'REGIONAL', 1 FROM WicLocations wl
    WHERE wl.DisplayName = N'Essen - BP1'
      AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Danny Bendig' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

    INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
    SELECT wl.LocationCode, N'Danny Bendig', N'REGIONAL', 1 FROM WicLocations wl
    WHERE wl.DisplayName = N'Essen - TK'
      AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Danny Bendig' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

    PRINT N'Danny Bendig: FOUND - added as REGIONAL to Essen - BP1 and Essen - TK.';
END
ELSE
BEGIN
    PRINT N'UNMATCHED - Danny Bendig not in Employees. Essen BP1/TK REGIONAL slot skipped.';
END

-- Ercan Akdeniz (Brokdorf REGIONAL)
IF EXISTS (SELECT 1 FROM Employees WHERE FullName = N'Ercan Akdeniz')
BEGIN
    INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
    SELECT wl.LocationCode, N'Ercan Akdeniz', N'REGIONAL', 1 FROM WicLocations wl
    WHERE wl.DisplayName = N'Brokdorf'
      AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Ercan Akdeniz' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

    PRINT N'Ercan Akdeniz: FOUND - added as REGIONAL to Brokdorf.';
END
ELSE
BEGIN
    PRINT N'UNMATCHED - Ercan Akdeniz not in Employees. Brokdorf REGIONAL slot skipped.';
END

PRINT N'Part 4 done.';
GO

-- ============================================================
-- PART 5: Employees GroupRegion
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 5: Employee GroupRegion...';

UPDATE Employees SET GroupRegion = N'Hamburg+Quickborn'
WHERE FullName = N'Bishal Maharjan';

UPDATE Employees SET GroupRegion = N'Essenbach+Emmerthal+Hannover+Brokdorf'
WHERE FullName IN (N'Holger Petzholdt', N'John Daniel Wendland', N'Adam Szilvagyi', N'Jannik Borner');

UPDATE Employees SET GroupRegion = N'Stade+Stadland'
WHERE FullName IN (N'Abdulrahman Aldera', N'Joel Broring');

UPDATE Employees SET GroupRegion = N'Augsburg+Regensburg+Pfaffenhofen'
WHERE FullName = N'Kamil Filipowicz';

UPDATE Employees SET GroupRegion = N'Augsburg+Regensburg+Pfaffenhofen+Bamberg'
WHERE FullName IN (N'Mariusz Kozinski', N'Binod Dutta', N'Marcus Rusch');

UPDATE Employees SET GroupRegion = N'Quickborn+Rendsburg'
WHERE FullName IN (N'Amir Nassri', N'Hamza Forrousso');

PRINT N'Part 5 done.';
GO

-- ============================================================
-- PART 6: Employees HasCar
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 6: Employee HasCar...';

UPDATE Employees SET HasCar = 1
WHERE FullName IN (N'Mahboubeh Abdighara', N'Angelika Weber');

PRINT N'Part 6 done.';
GO

-- ============================================================
-- SUMMARY counts (read-only, inside open transaction)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== COUNTS ===';
SELECT N'WicAgentAssignments active'   AS [Tbl], COUNT(*) AS [Rows] FROM WicAgentAssignments WHERE IsActive = 1;
SELECT N'WicAgentAssignments inactive' AS [Tbl], COUNT(*) AS [Rows] FROM WicAgentAssignments WHERE IsActive = 0;
SELECT N'Employees with GroupRegion'   AS [Tbl], COUNT(*) AS [Rows] FROM Employees WHERE GroupRegion IS NOT NULL AND GroupRegion <> N'';
SELECT N'Employees with HasCar=1'      AS [Tbl], COUNT(*) AS [Rows] FROM Employees WHERE HasCar = 1;
PRINT N'';
PRINT N'ALWAYS UNMATCHED: Michael Moeller (Demmin WICs backup - no name match in Employees)';
PRINT N'UNCERTAIN APPLIED: Negin Bazmi (source had trailing ? - treated as Negin Bazmi)';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== WIC PLAN UPDATE committed successfully ===';
END
ELSE
    PRINT N'=== ERROR: Transaction was rolled back. No changes committed. ===';
GO
