-- ============================================================
-- Employee Sync — 2026-07-10
-- Source: GSD Master Sheet (Outlook / Excel)
-- 58 employees from the ID+Name roster
--
-- Logic:
--   - INSERT if neither EmployeeId nor FullName already exists in DB
--   - SKIP silently if EmployeeId already exists (shift-import may have added it)
--   - SKIP and WARN (Part 1) if FullName exists under a different EmployeeId
--     → those rows need manual EmployeeId reconciliation
--   - Tim Nguyen (9078602): absence records imported earlier get EmployeeId backfilled
--
-- DayOfWeek key: EmployeeIds stored as NVARCHAR matching the sheet's numeric IDs
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== Employee Sync 2026-07-10 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 1: Pre-check — sheet employees that exist in DB by name
--         but potentially under a different EmployeeId
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Pre-check — FullName conflicts...';

WITH SheetData AS (
    SELECT * FROM (VALUES
        (N'9074381', N'Eva-Liane Schliwa'),
        (N'9074348', N'Asal Wardaastiani Azar'),
        (N'9074363', N'Danny Bendig'),
        (N'9074590', N'Tri Toan Nguyen'),
        (N'9114618', N'Mustafa Deveci'),
        (N'9074375', N'Elena Schlosser'),
        (N'9074535', N'Lubomir Stoyanov'),
        (N'9074373', N'Duc Quy Huynh'),
        (N'9086658', N'Vincent Grunzel'),
        (N'9087657', N'Meik ' + NCHAR(350) + N'lgen'),  -- Schülgen
        (N'9126877', N'Ahmed Hasanovic'),
        (N'9074576', N'Sharon Huber'),
        (N'9074582', N'Stephan Becker'),
        (N'9074352', N'Burak Kurtulmaz'),
        (N'9075030', N'Kevin Haska'),
        (N'9074528', N'Kolja Christlieb'),
        (N'9114617', N'Christian Pastors'),
        (N'9074364', N'Darjusch Dropczinsky'),
        (N'9078602', N'Tim Nguyen'),
        (N'9085121', N'Walter Buxbaum'),
        (N'9090511', N'Annabela Scavo'),
        (N'9074428', N'Yevgeni Frenkel'),
        (N'3193177', N'Gunter Dinkelmann'),
        (N'3193175', N'Isloodien Hurchem Lawrence'),
        (N'3193174', N'Anifa Ngcongo'),
        (N'3193178', N'Samantha Buys'),
        (N'3193180', N'Cortneigh Halim'),
        (N'9083024', N'Ercan Akdeniz'),
        (N'9124690', N'Boris Kostov'),
        (N'9125526', N'Kemal Sener'),
        (N'9092596', N'Timon Philippen'),
        (N'9074518', N'Javier Sang'),
        (N'9074592', N'Victoria Scholz'),
        (N'9074519', N'Jessica Schlicht'),
        (N'9085138', N'Christoph Ulatowski'),
        (N'9090513', N'Michael Holz'),
        (N'9074559', N'Perim Rollin'),
        (N'9090514', N'Sam Alisha Metzner'),
        (N'9128158', N'Erne Kis'),
        (N'9124695', N'Veronika Kouwui'),
        (N'9074356', N'Christian Koch'),
        (N'9074341', N'Anas Daba'),
        (N'9074334', N'Aleksandrina Dencheva'),
        (N'9074350', N'Baschir Mahrufi'),
        (N'9120970', N'Amani Kedo'),
        (N'9124687', N'Zehra Sila G' + NCHAR(246) + N'rg' + NCHAR(252) + N'n'),  -- Görgün
        (N'9074330', N'Adnan Lelic'),
        (N'9074526', N'Kevin Heynen'),
        (N'9133999', N'Marko Bosnjak'),
        (N'9074549', N'Mitko Kilogramski'),
        (N'9076905', N'Arevig Ketenjian'),
        (N'9086366', N'Tarek Tabbara'),
        (N'9119463', N'Jonathan Freudenthaler'),
        (N'9126887', N'Dominik Bajic'),
        (N'9074563', N'Ralf Turski'),
        (N'9085123', N'Anil Bedzeti'),
        (N'9074557', N'Pascal Dutz'),
        (N'9074345', N'Angelika Weber')
    ) AS t(SheetId, SheetName)
)
SELECT
    sd.SheetId        AS SheetEmployeeId,
    sd.SheetName,
    e.EmployeeId      AS DB_EmployeeId,
    CASE
        WHEN e.EmployeeId = sd.SheetId THEN N'OK — IDs match'
        ELSE N'WARN — name exists but EmployeeId differs; sheet=' + sd.SheetId + N' db=' + e.EmployeeId
    END AS Status
FROM SheetData sd
INNER JOIN Employees e ON e.FullName = sd.SheetName
ORDER BY Status DESC, sd.SheetName;

PRINT N'Part 1 done. Rows above need review if Status = WARN.';
GO

-- ============================================================
-- PART 2: Bulk INSERT — all 58 employees
--   Skipped per row if EmployeeId OR FullName already in DB.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Inserting new employees...';

WITH src AS (
    SELECT * FROM (VALUES
        -- NEW employees (not in existing WIC agent seeds)
        (N'9074381', N'Eva-Liane',          N'Schliwa',              N'Eva-Liane Schliwa'),
        (N'9074348', N'Asal Wardaastiani',  N'Azar',                 N'Asal Wardaastiani Azar'),
        (N'9074363', N'Danny',              N'Bendig',               N'Danny Bendig'),
        (N'9074590', N'Tri Toan',           N'Nguyen',               N'Tri Toan Nguyen'),
        (N'9114618', N'Mustafa',            N'Deveci',               N'Mustafa Deveci'),
        (N'9074375', N'Elena',              N'Schlosser',            N'Elena Schlosser'),
        (N'9074535', N'Lubomir',            N'Stoyanov',             N'Lubomir Stoyanov'),
        (N'9074373', N'Duc Quy',            N'Huynh',                N'Duc Quy Huynh'),
        (N'9086658', N'Vincent',            N'Grunzel',              N'Vincent Grunzel'),
        (N'9087657', N'Meik',               N'Sch' + NCHAR(252) + N'lgen',   N'Meik Sch' + NCHAR(252) + N'lgen'),
        (N'9126877', N'Ahmed',              N'Hasanovic',            N'Ahmed Hasanovic'),
        (N'9074576', N'Sharon',             N'Huber',                N'Sharon Huber'),
        (N'9074582', N'Stephan',            N'Becker',               N'Stephan Becker'),
        (N'9075030', N'Kevin',              N'Haska',                N'Kevin Haska'),
        (N'9074528', N'Kolja',              N'Christlieb',           N'Kolja Christlieb'),
        (N'9114617', N'Christian',          N'Pastors',              N'Christian Pastors'),
        (N'9074364', N'Darjusch',           N'Dropczinsky',          N'Darjusch Dropczinsky'),
        (N'9078602', N'Tim',                N'Nguyen',               N'Tim Nguyen'),
        (N'9085121', N'Walter',             N'Buxbaum',              N'Walter Buxbaum'),
        (N'9090511', N'Annabela',           N'Scavo',                N'Annabela Scavo'),
        (N'9074428', N'Yevgeni',            N'Frenkel',              N'Yevgeni Frenkel'),
        (N'3193177', N'Gunter',             N'Dinkelmann',           N'Gunter Dinkelmann'),
        (N'3193175', N'Isloodien Hurchem',  N'Lawrence',             N'Isloodien Hurchem Lawrence'),
        (N'3193174', N'Anifa',              N'Ngcongo',              N'Anifa Ngcongo'),
        (N'3193178', N'Samantha',           N'Buys',                 N'Samantha Buys'),
        (N'3193180', N'Cortneigh',          N'Halim',                N'Cortneigh Halim'),
        (N'9083024', N'Ercan',              N'Akdeniz',              N'Ercan Akdeniz'),
        (N'9124690', N'Boris',              N'Kostov',               N'Boris Kostov'),
        (N'9125526', N'Kemal',              N'Sener',                N'Kemal Sener'),
        (N'9092596', N'Timon',              N'Philippen',            N'Timon Philippen'),
        (N'9074518', N'Javier',             N'Sang',                 N'Javier Sang'),
        (N'9074592', N'Victoria',           N'Scholz',               N'Victoria Scholz'),
        (N'9074519', N'Jessica',            N'Schlicht',             N'Jessica Schlicht'),
        (N'9074559', N'Perim',              N'Rollin',               N'Perim Rollin'),
        (N'9090514', N'Sam Alisha',         N'Metzner',              N'Sam Alisha Metzner'),
        (N'9128158', N'Erne',               N'Kis',                  N'Erne Kis'),
        (N'9124695', N'Veronika',           N'Kouwui',               N'Veronika Kouwui'),
        (N'9074356', N'Christian',          N'Koch',                 N'Christian Koch'),
        (N'9074341', N'Anas',               N'Daba',                 N'Anas Daba'),
        (N'9074334', N'Aleksandrina',       N'Dencheva',             N'Aleksandrina Dencheva'),
        (N'9074350', N'Baschir',            N'Mahrufi',              N'Baschir Mahrufi'),
        (N'9124687', N'Zehra Sila',         N'G' + NCHAR(246) + N'rg' + NCHAR(252) + N'n',  N'Zehra Sila G' + NCHAR(246) + N'rg' + NCHAR(252) + N'n'),
        (N'9074330', N'Adnan',              N'Lelic',                N'Adnan Lelic'),
        (N'9133999', N'Marko',              N'Bosnjak',              N'Marko Bosnjak'),
        (N'9074549', N'Mitko',              N'Kilogramski',          N'Mitko Kilogramski'),
        (N'9076905', N'Arevig',             N'Ketenjian',            N'Arevig Ketenjian'),
        (N'9086366', N'Tarek',              N'Tabbara',              N'Tarek Tabbara'),
        (N'9119463', N'Jonathan',           N'Freudenthaler',        N'Jonathan Freudenthaler'),
        (N'9126887', N'Dominik',            N'Bajic',                N'Dominik Bajic'),
        (N'9074563', N'Ralf',               N'Turski',               N'Ralf Turski'),
        (N'9085123', N'Anil',               N'Bedzeti',              N'Anil Bedzeti'),
        (N'9074557', N'Pascal',             N'Dutz',                 N'Pascal Dutz'),
        -- Also in WIC seeds — skipped if already present
        (N'9074352', N'Burak',              N'Kurtulmaz',            N'Burak Kurtulmaz'),
        (N'9085138', N'Christoph',          N'Ulatowski',            N'Christoph Ulatowski'),
        (N'9090513', N'Michael',            N'Holz',                 N'Michael Holz'),
        (N'9120970', N'Amani',              N'Kedo',                 N'Amani Kedo'),
        (N'9074526', N'Kevin',              N'Heynen',               N'Kevin Heynen'),
        (N'9074345', N'Angelika',           N'Weber',                N'Angelika Weber')
    ) AS t(EmpId, FName, LName, FulName)
)
INSERT INTO Employees (EmployeeId, FirstName, LastName, FullName, IsActive, SourceSheet, CreatedAt)
SELECT src.EmpId, src.FName, src.LName, src.FulName, 1, N'GSD_DE', GETDATE()
FROM src
WHERE NOT EXISTS (
    SELECT 1 FROM Employees e
    WHERE e.EmployeeId = src.EmpId
       OR e.FullName   = src.FulName
);

PRINT N'Inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' new employees.';
PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: Backfill Tim Nguyen EmployeeId in Vacations
--   Absence import (absence_import_20260710.sql) inserted vacation
--   records for Tim Nguyen with NULL EmployeeId because he was
--   not yet in Employees. Now that he is, link them.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: Backfill Tim Nguyen EmployeeId in Vacations...';

UPDATE Vacations
SET EmployeeId = N'9078602'
WHERE FirstName = N'Tim'
  AND LastName  = N'Nguyen'
  AND EmployeeId IS NULL;

PRINT N'Updated: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' vacation rows for Tim Nguyen.';
PRINT N'Part 3 done.';
GO

-- ============================================================
-- PART 4: Danny Bendig — wic_update.sql pending assignment
--   wic_update.sql added Danny Bendig to Essen BP1 + TK if
--   he existed in Employees. Run that conditional block now.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 4: Danny Bendig — Essen WIC assignments...';

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

    PRINT N'Danny Bendig: REGIONAL added to Essen - BP1 and Essen - TK.';
END
ELSE
    PRINT N'Danny Bendig: still not in Employees after sync — check name spelling.';
GO

-- ============================================================
-- PART 5: Ercan Akdeniz — wic_update.sql pending assignment
--   wic_update.sql added Ercan Akdeniz to Brokdorf REGIONAL
--   conditionally. Resolve it now.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 5: Ercan Akdeniz — Brokdorf REGIONAL assignment...';

IF EXISTS (SELECT 1 FROM Employees WHERE FullName = N'Ercan Akdeniz')
BEGIN
    INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
    SELECT wl.LocationCode, N'Ercan Akdeniz', N'REGIONAL', 1 FROM WicLocations wl
    WHERE wl.DisplayName = N'Brokdorf'
      AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Ercan Akdeniz' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

    PRINT N'Ercan Akdeniz: REGIONAL added to Brokdorf.';
END
ELSE
    PRINT N'Ercan Akdeniz: still not in Employees after sync — check name spelling.';
GO

-- ============================================================
-- PART 6: Jonathan Freudenthaler — wic_update.sql pending
--   Arnsberg MAIN + Mülheim BACKUP
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 6: Jonathan Freudenthaler — Arnsberg / M' + NCHAR(252) + N'lheim assignments...';

IF EXISTS (SELECT 1 FROM Employees WHERE FullName = N'Jonathan Freudenthaler')
BEGIN
    -- Arnsberg: deactivate Angelika Weber MAIN (she moves to REGIONAL)
    UPDATE waa SET IsActive = 0
    FROM WicAgentAssignments waa INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
    WHERE wl.DisplayName = N'Arnsberg' AND waa.AssignmentType = N'MAIN'
      AND waa.EmployeeName = N'Angelika Weber' AND waa.IsActive = 1;

    -- Arnsberg: Jonathan MAIN
    INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
    SELECT wl.LocationCode, N'Jonathan Freudenthaler', N'MAIN', 1 FROM WicLocations wl
    WHERE wl.DisplayName = N'Arnsberg'
      AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Jonathan Freudenthaler' AND e.AssignmentType = N'MAIN' AND e.IsActive = 1);

    -- Arnsberg: Angelika REGIONAL
    INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
    SELECT wl.LocationCode, N'Angelika Weber', N'REGIONAL', 1 FROM WicLocations wl
    WHERE wl.DisplayName = N'Arnsberg'
      AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Angelika Weber' AND e.AssignmentType = N'REGIONAL' AND e.IsActive = 1);

    -- Mülheim: Jonathan BACKUP
    INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive)
    SELECT wl.LocationCode, N'Jonathan Freudenthaler', N'BACKUP', 1 FROM WicLocations wl
    WHERE wl.DisplayName = N'M' + NCHAR(252) + N'lheim'
      AND NOT EXISTS (SELECT 1 FROM WicAgentAssignments e WHERE e.LocationCode = wl.LocationCode AND e.EmployeeName = N'Jonathan Freudenthaler' AND e.AssignmentType = N'BACKUP' AND e.IsActive = 1);

    PRINT N'Jonathan Freudenthaler: Arnsberg MAIN + M' + NCHAR(252) + N'lheim BACKUP done. Angelika Weber -> REGIONAL.';
END
ELSE
    PRINT N'Jonathan Freudenthaler: still not found after sync — check name spelling.';
GO

-- ============================================================
-- PART 7: Confirmation
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== EMPLOYEE SYNC RESULTS ===';

SELECT N'Total active employees' AS Metric, COUNT(*) AS Value FROM Employees WHERE IsActive = 1
UNION ALL
SELECT N'Employees with SourceSheet=GSD_DE', COUNT(*) FROM Employees WHERE SourceSheet = N'GSD_DE' AND IsActive = 1
UNION ALL
SELECT N'Employees with no EmployeeId in Vacations (orphaned)', COUNT(*) FROM Vacations WHERE EmployeeId IS NULL;

-- Newly inserted employees from this run
SELECT
    e.Id,
    e.EmployeeId,
    e.FullName,
    e.SourceSheet,
    CONVERT(NVARCHAR(20), e.CreatedAt, 120) AS CreatedAt
FROM Employees e
WHERE e.SourceSheet = N'GSD_DE'
  AND CAST(e.CreatedAt AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY e.FullName;

PRINT N'';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== Employee Sync committed successfully ===';
END
ELSE
    PRINT N'=== ERROR: Transaction was rolled back. No changes committed. ===';
GO
