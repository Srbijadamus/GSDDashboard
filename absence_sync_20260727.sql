-- ============================================================
-- Absence Sync -- 2026-07-27
-- Source: Absence Overview (confirmed accurate for today)
-- Fix v2: LastDay uses '2099-12-31' (column NOT NULL); NCHAR for umlauts
--
-- SL  (5): Tri Toan Nguyen, Anas Daba, Pascal Dutz,
--          Sebastian Hoeck, Tim Boger
-- AL (15): Meik Schuelgen, Sharon Huber, Ercan Akdeniz,
--          Javier Sang, Michael Holz, Baschir Mahrufi,
--          Adnan Lelic, Anil Bedzeti, Ayten Karatas,
--          Dennis Markus, Dmytro Shelikhov, Holger Petzholdt,
--          Joel Broring, Sebastian Lewandowski
--          Kavinraj Pathmanathan -- MISSING in DB, skipped
--          Michael Holz listed twice in source -- deduped to 15 unique
-- OFF/OL/CD (5): Mustafa Deveci, Kemal Sener, Veronika Kouwui,
--                Zehra Sila Goergun, Negin Bazmi
-- UL  (0): none
-- Night (1): Aleksandrina Dencheva
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== Absence Sync 2026-07-27 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 1: Gap report
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Employee lookup...';

SELECT
    N'SL' AS AbsType, e.FullName, N'OK - ' + e.EmployeeId AS Status
FROM Employees e WHERE e.EmployeeId IN (N'9074590',N'9074341',N'9074557',N'9125521') AND e.IsActive=1
UNION ALL
SELECT N'SL', N'Sebastian H'+NCHAR(246)+N'ck',
    CASE WHEN e.EmployeeId IS NOT NULL THEN N'OK - '+e.EmployeeId ELSE N'MISSING' END
FROM Employees e WHERE e.FullName = N'Sebastian H'+NCHAR(246)+N'ck' AND e.IsActive=1
UNION ALL
SELECT N'AL (MISSING)', N'Kavinraj Pathmanathan', N'MISSING - skipped'
WHERE NOT EXISTS (SELECT 1 FROM Employees WHERE FullName=N'Kavinraj Pathmanathan' AND IsActive=1)
ORDER BY AbsType, Status;

PRINT N'Part 1 done.';
GO

-- ============================================================
-- PART 2: SickLeave records (LastDay=2099-12-31 = open-ended)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: SickLeave records...';

-- Tri Toan Nguyen (9074590)
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT e.EmployeeId, N'Tri Toan', N'Nguyen', e.TeamLeadName,
    '2026-07-27', '2099-12-31', N'Self',
    N'SL 2026-07-27. Open-ended (LastDay=2099-12-31); update when return confirmed.',
    N'GSD_DE', GETDATE()
FROM Employees e WHERE e.EmployeeId = N'9074590'
  AND NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId=N'9074590'
      AND FirstDay<='2026-07-27' AND LastDay>='2026-07-27');

-- Anas Daba (9074341)
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT e.EmployeeId, N'Anas', N'Daba', e.TeamLeadName,
    '2026-07-27', '2099-12-31', N'Self',
    N'SL 2026-07-27. Open-ended; update when return confirmed.',
    N'GSD_DE', GETDATE()
FROM Employees e WHERE e.EmployeeId = N'9074341'
  AND NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId=N'9074341'
      AND FirstDay<='2026-07-27' AND LastDay>='2026-07-27');

-- Tim Boger (9125521) - was AL on 2026-07-10, now SL
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT e.EmployeeId, N'Tim', N'Boger', e.TeamLeadName,
    '2026-07-27', '2099-12-31', N'Self',
    N'SL 2026-07-27. Was AL 2026-07-10. Grafenrheinfeld WIC main uncovered.',
    N'GSD_DE', GETDATE()
FROM Employees e WHERE e.EmployeeId = N'9125521'
  AND NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId=N'9125521'
      AND FirstDay<='2026-07-27' AND LastDay>='2026-07-27');

-- Sebastian Hoeck -- lookup by name with NCHAR
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT e.EmployeeId, N'Sebastian', N'H'+NCHAR(246)+N'ck', e.TeamLeadName,
    '2026-07-27', '2099-12-31', N'Self',
    N'SL open from 2026-07-10 -- new period record if prior one closed.',
    N'GSD_DE', GETDATE()
FROM Employees e WHERE e.FullName = N'Sebastian H'+NCHAR(246)+N'ck' AND e.IsActive=1
  AND NOT EXISTS (SELECT 1 FROM SickLeaves sl
      WHERE sl.EmployeeId=e.EmployeeId
        AND sl.FirstDay<='2026-07-27' AND sl.LastDay>='2026-07-27');

PRINT N'Pascal Dutz (9074557) already has open SL -- skipped.';
PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: ShiftEntries -- SL (all 5 by EmployeeId where known)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: ShiftEntries for SL...';

INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, SourceSheet)
SELECT e.EmployeeId, '2026-07-27', N'SL', N'SL', N'GSD_DE'
FROM Employees e
WHERE e.EmployeeId IN (N'9074590',N'9074341',N'9074557',N'9125521')
  AND NOT EXISTS (SELECT 1 FROM ShiftEntries se
      WHERE se.EmployeeId=e.EmployeeId AND se.ShiftDate='2026-07-27' AND se.SourceSheet=N'GSD_DE');

-- Sebastian Hoeck by name
INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, SourceSheet)
SELECT e.EmployeeId, '2026-07-27', N'SL', N'SL', N'GSD_DE'
FROM Employees e WHERE e.FullName = N'Sebastian H'+NCHAR(246)+N'ck' AND e.IsActive=1
  AND NOT EXISTS (SELECT 1 FROM ShiftEntries se
      WHERE se.EmployeeId=e.EmployeeId AND se.ShiftDate='2026-07-27' AND se.SourceSheet=N'GSD_DE');

PRINT N'SL ShiftEntries done.';
GO

-- ============================================================
-- PART 4: ShiftEntries -- AL (14 by EmployeeId + Meik by name)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 4: ShiftEntries for AL...';

-- Known IDs (Kavinraj Pathmanathan excluded - MISSING in DB)
INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, SourceSheet)
SELECT e.EmployeeId, '2026-07-27', N'AL', N'AL', N'GSD_DE'
FROM Employees e
WHERE e.EmployeeId IN (
    N'9047339',  -- Ayten Karatas
    N'9074330',  -- Adnan Lelic
    N'9074350',  -- Baschir Mahrufi
    N'9074518',  -- Javier Sang
    N'9074576',  -- Sharon Huber
    N'9083024',  -- Ercan Akdeniz
    N'9085123',  -- Anil Bedzeti
    N'9090513',  -- Michael Holz
    N'9107615',  -- Dennis Markus
    N'9125516',  -- Joel Broring
    N'9125517',  -- Holger Petzholdt
    N'9130657',  -- Dmytro Shelikhov
    N'9132079'   -- Sebastian Lewandowski
)
  AND NOT EXISTS (SELECT 1 FROM ShiftEntries se
      WHERE se.EmployeeId=e.EmployeeId AND se.ShiftDate='2026-07-27' AND se.SourceSheet=N'GSD_DE');

-- Meik Schuelgen by name (umlaut)
INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, SourceSheet)
SELECT e.EmployeeId, '2026-07-27', N'AL', N'AL', N'GSD_DE'
FROM Employees e WHERE e.FullName = N'Meik Sch'+NCHAR(252)+N'lgen' AND e.IsActive=1
  AND NOT EXISTS (SELECT 1 FROM ShiftEntries se
      WHERE se.EmployeeId=e.EmployeeId AND se.ShiftDate='2026-07-27' AND se.SourceSheet=N'GSD_DE');

PRINT N'AL ShiftEntries done.';
GO

-- ============================================================
-- PART 5: ShiftEntries -- OFF/OL/CD (5)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 5: ShiftEntries for OFF...';

-- Known IDs
INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, SourceSheet)
SELECT e.EmployeeId, '2026-07-27', N'OFF/OL/CD', N'OFF', N'GSD_DE'
FROM Employees e
WHERE e.EmployeeId IN (
    N'9114618',  -- Mustafa Deveci
    N'9125526',  -- Kemal Sener
    N'9124695',  -- Veronika Kouwui
    N'9126886'   -- Negin Bazmi
)
  AND NOT EXISTS (SELECT 1 FROM ShiftEntries se
      WHERE se.EmployeeId=e.EmployeeId AND se.ShiftDate='2026-07-27' AND se.SourceSheet=N'GSD_DE');

-- Zehra Sila Goergun by name (umlaut)
INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, SourceSheet)
SELECT e.EmployeeId, '2026-07-27', N'OFF/OL/CD', N'OFF', N'GSD_DE'
FROM Employees e WHERE e.FullName = N'Zehra Sila G'+NCHAR(246)+N'rg'+NCHAR(252)+N'n' AND e.IsActive=1
  AND NOT EXISTS (SELECT 1 FROM ShiftEntries se
      WHERE se.EmployeeId=e.EmployeeId AND se.ShiftDate='2026-07-27' AND se.SourceSheet=N'GSD_DE');

PRINT N'OFF ShiftEntries done.';
GO

-- ============================================================
-- PART 6: Night shift -- Aleksandrina Dencheva (9074334)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 6: Night shift...';

INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, ShiftStart, ShiftEnd, SourceSheet)
SELECT e.EmployeeId, '2026-07-27', N'Night', N'WORKING', N'22:00', N'07:00', N'GSD_DE'
FROM Employees e WHERE e.EmployeeId = N'9074334'
  AND NOT EXISTS (SELECT 1 FROM ShiftEntries se
      WHERE se.EmployeeId=e.EmployeeId AND se.ShiftDate='2026-07-27' AND se.SourceSheet=N'GSD_DE');

PRINT N'Part 6 done.';
GO

-- ============================================================
-- PART 7: Final report
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== RESULTS FOR 2026-07-27 ===';

SELECT N'SickLeave' AS [Type],
    sl.EmployeeId, sl.FirstName+N' '+sl.LastName AS Name,
    CONVERT(NVARCHAR(10),sl.FirstDay,104) AS FirstDay,
    CONVERT(NVARCHAR(10),sl.LastDay,104) AS LastDay,
    sl.Comments
FROM SickLeaves sl
WHERE sl.FirstDay='2026-07-27'
ORDER BY Name;

SELECT se.ShiftType AS [Type], e.FullName AS Name,
    ISNULL(se.ShiftStart+N' - '+se.ShiftEnd, N'') AS Shift
FROM ShiftEntries se
INNER JOIN Employees e ON e.EmployeeId=se.EmployeeId
WHERE se.ShiftDate='2026-07-27' AND se.SourceSheet=N'GSD_DE'
ORDER BY se.ShiftType, e.FullName;

PRINT N'';
PRINT N'OPEN: Kavinraj Pathmanathan not found in DB -- add employee record manually.';
PRINT N'OPEN: Update SL LastDay from 2099-12-31 when return/AU date confirmed.';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== Absence Sync committed -- 2026-07-27 ===';
END
ELSE
    PRINT N'=== ERROR: Rolled back. ===';
GO
