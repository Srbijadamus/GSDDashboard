-- ============================================================
-- Absence Sync — 2026-07-10
-- Source: External Absence Overview (confirmed accurate for today)
--
-- Gaps vs our DB:
--   SL:  3 missing entirely + 2 extensions (Dutz, Bachmann ended Jul 3)
--   AL:  18 — none in DB for today (all our AL records are future-dated)
--   OFF: 11 — none in DB for today
--   Night: 1 (Asal Wardaastiani Azar)
--   New employee: Yiting Qiang (not in any prior list)
--
-- Strategy:
--   SL  → SickLeaves table  (LastDay NULL for open-ended; update when AU arrives)
--   AL  → ShiftEntries only (ShiftType='AL') — Vacations table needs full period
--   OFF → ShiftEntries (ShiftType='OFF')
--   Night → ShiftEntries (ShiftType='WORKING', night shift times)
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== Absence Sync 2026-07-10 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 1: Gap report — compare external snapshot vs DB
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Gap analysis for 2026-07-10...';

-- SL check: who appears in external SL list but has no active SL in DB today
WITH ExtSL AS (
    SELECT * FROM (VALUES
        (N'Annabela Scavo'),
        (N'Pascal Dutz'),
        (N'John Daniel Wendland'),
        (N'Mark Bachmann'),
        (N'Sebastian H' + NCHAR(246) + N'ck')
    ) AS t(FullName)
)
SELECT
    e.FullName,
    ISNULL(e.EmployeeId, N'?') AS EmployeeId,
    CASE
        WHEN sl.Id IS NULL THEN N'MISSING — no SL record covers today'
        WHEN sl.LastDay < '2026-07-10' THEN N'EXPIRED — last SL ended ' + CONVERT(NVARCHAR(10), sl.LastDay, 104)
        ELSE N'OK'
    END AS SL_Status,
    ISNULL(CAST(sl.Id AS NVARCHAR(10)), N'—') AS SL_Id
FROM ExtSL x
LEFT JOIN Employees e ON e.FullName = x.FullName
LEFT JOIN SickLeaves sl
    ON sl.EmployeeId = e.EmployeeId
   AND sl.FirstDay  <= '2026-07-10'
   AND (sl.LastDay  >= '2026-07-10' OR sl.LastDay IS NULL)
ORDER BY SL_Status, x.FullName;

-- AL check: who is on AL today but has no AL ShiftEntry for today
WITH ExtAL AS (
    SELECT * FROM (VALUES
        (N'Yiting Qiang'),           (N'Delia Panaitescu'),
        (N'Mustafa Deveci'),          (N'Duc Quy Huynh'),
        (N'Timon Philippen'),         (N'Christian Koch'),
        (N'Zehra Sila G' + NCHAR(246) + N'rg' + NCHAR(252) + N'n'),
        (N'Adnan Lelic'),             (N'Kevin Heynen'),
        (N'Mitko Kilogramski'),       (N'Elaheh Ramzi'),
        (N'Francois Sicot'),          (N'Ion Bodnariuc'),
        (N'Kavinraj Pathmanathan'),   (N'Merlin Voss'),
        (N'Tim Boger'),               (N'Yun Hee Oh'),
        (N'Viktor Winter')
    ) AS t(FullName)
)
SELECT
    x.FullName,
    ISNULL(e.EmployeeId, N'MISSING IN DB') AS EmployeeId,
    CASE
        WHEN e.EmployeeId IS NULL THEN N'EMPLOYEE NOT IN DB'
        WHEN se.Id IS NULL        THEN N'NO AL SHIFTENTRY FOR TODAY'
        ELSE                           N'OK'
    END AS AL_Status
FROM ExtAL x
LEFT JOIN Employees e  ON e.FullName   = x.FullName
LEFT JOIN ShiftEntries se
    ON se.EmployeeId  = e.EmployeeId
   AND se.ShiftDate   = '2026-07-10'
   AND se.ShiftType   = N'AL'
ORDER BY AL_Status, x.FullName;

PRINT N'Part 1 done.';
GO

-- ============================================================
-- PART 2: Add Yiting Qiang — new employee not in any prior list
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Yiting Qiang — new employee...';

INSERT INTO Employees (EmployeeId, FirstName, LastName, FullName, IsActive, SourceSheet, CreatedAt)
SELECT N'UNKNOWN_YQ', N'Yiting', N'Qiang', N'Yiting Qiang', 1, N'GSD_DE', GETDATE()
WHERE NOT EXISTS (
    SELECT 1 FROM Employees WHERE FullName = N'Yiting Qiang'
);

PRINT N'NOTE: Yiting Qiang inserted with placeholder EmployeeId=UNKNOWN_YQ.';
PRINT N'      Update EmployeeId once the numeric ID is confirmed from HR.';
PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: SickLeave extensions + missing SL records
--   Pascal Dutz  (9074557): had SL Jun26-Jul3 → extension Jul6-?
--   Mark Bachmann(9122675): had SL Jun26-Jul3 → extension Jul6-?
--   Annabela Scavo, John Daniel Wendland, Sebastian Höck: no prior SL in DB
--   LastDay = NULL for open-ended (update when AU/return date known)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: SL extensions and missing SL records...';

-- Pascal Dutz — extension from Jul 6
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT N'9074557', N'Pascal', N'Dutz',
    (SELECT TOP 1 TeamLeadName FROM Employees WHERE EmployeeId = N'9074557'),
    '2026-07-06', NULL,
    N'Self',
    N'SL extension — prior period 26-Jun/03-Jul. LastDay open; update when AU received.',
    N'GSD_DE', GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId = N'9074557' AND FirstDay = '2026-07-06');

-- Mark Bachmann — extension from Jul 6
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT N'9122675', N'Mark', N'Bachmann',
    (SELECT TOP 1 TeamLeadName FROM Employees WHERE EmployeeId = N'9122675'),
    '2026-07-06', NULL,
    N'Self',
    N'SL extension — prior period 26-Jun/03-Jul. LastDay open; update when AU received.',
    N'GSD_DE', GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId = N'9122675' AND FirstDay = '2026-07-06');

-- Annabela Scavo — new SL, start date unknown; using today as anchor
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT e.EmployeeId, N'Annabela', N'Scavo', e.TeamLeadName,
    '2026-07-10', NULL,
    N'Self',
    N'SL reported via Absence Overview 2026-07-10. Exact start date unconfirmed; update FirstDay if earlier.',
    N'GSD_DE', GETDATE()
FROM Employees e WHERE e.FullName = N'Annabela Scavo'
  AND NOT EXISTS (SELECT 1 FROM SickLeaves sl
      WHERE sl.EmployeeId = e.EmployeeId AND sl.FirstDay = '2026-07-10');

-- John Daniel Wendland — new SL
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT e.EmployeeId, N'John Daniel', N'Wendland', e.TeamLeadName,
    '2026-07-10', NULL,
    N'Self',
    N'SL reported via Absence Overview 2026-07-10. Exact start date unconfirmed; update FirstDay if earlier.',
    N'GSD_DE', GETDATE()
FROM Employees e WHERE e.FullName = N'John Daniel Wendland'
  AND NOT EXISTS (SELECT 1 FROM SickLeaves sl
      WHERE sl.EmployeeId = e.EmployeeId AND sl.FirstDay = '2026-07-10');

-- Sebastian Höck — new SL
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT e.EmployeeId, N'Sebastian', N'H' + NCHAR(246) + N'ck', e.TeamLeadName,
    '2026-07-10', NULL,
    N'Self',
    N'SL reported via Absence Overview 2026-07-10. Exact start date unconfirmed; update FirstDay if earlier.',
    N'GSD_DE', GETDATE()
FROM Employees e WHERE e.FullName = N'Sebastian H' + NCHAR(246) + N'ck'
  AND NOT EXISTS (SELECT 1 FROM SickLeaves sl
      WHERE sl.EmployeeId = e.EmployeeId AND sl.FirstDay = '2026-07-10');

PRINT N'Part 3 done.';
GO

-- ============================================================
-- PART 4: ShiftEntries for today's AL (18 employees)
--   ShiftType = 'AL'. Full Vacations period unknown; use ShiftEntry
--   as day-level marker. Insert Vacations records separately once
--   full AL periods are confirmed.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 4: AL ShiftEntries for 2026-07-10...';

WITH AlToday AS (
    SELECT * FROM (VALUES
        (N'Yiting Qiang'),
        (N'Delia Panaitescu'),
        (N'Mustafa Deveci'),
        (N'Duc Quy Huynh'),
        (N'Timon Philippen'),
        (N'Christian Koch'),
        (N'Zehra Sila G' + NCHAR(246) + N'rg' + NCHAR(252) + N'n'),
        (N'Adnan Lelic'),
        (N'Kevin Heynen'),
        (N'Mitko Kilogramski'),
        (N'Elaheh Ramzi'),
        (N'Francois Sicot'),
        (N'Ion Bodnariuc'),
        (N'Kavinraj Pathmanathan'),
        (N'Merlin Voss'),
        (N'Tim Boger'),
        (N'Yun Hee Oh'),
        (N'Viktor Winter')
    ) AS t(FullName)
)
INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, SourceSheet)
SELECT e.EmployeeId, '2026-07-10', N'AL', N'AL', N'GSD_DE'
FROM AlToday a
INNER JOIN Employees e ON e.FullName = a.FullName AND e.IsActive = 1
WHERE NOT EXISTS (
    SELECT 1 FROM ShiftEntries se
    WHERE se.EmployeeId  = e.EmployeeId
      AND se.ShiftDate   = '2026-07-10'
      AND se.SourceSheet = N'GSD_DE'
);

PRINT N'AL entries inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
PRINT N'Part 4 done.';
GO

-- ============================================================
-- PART 5: ShiftEntries for today's OFF/OL/CD (11 employees)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 5: OFF ShiftEntries for 2026-07-10...';

WITH OffToday AS (
    SELECT * FROM (VALUES
        (N'Tri Toan Nguyen'),
        (N'Christian Pastors'),
        (N'Veronika Kouwui'),
        (N'Arevig Ketenjian'),
        (N'Tarek Tabbara'),
        (N'Jonathan Freudenthaler'),
        (N'Dominik Bajic'),
        (N'Angelika Weber'),
        (N'Hamza Forrousso'),
        (N'Krishnendu Das'),
        (N'Negin Bazmi')
    ) AS t(FullName)
)
INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, SourceSheet)
SELECT e.EmployeeId, '2026-07-10', N'OFF/OL/CD', N'OFF', N'GSD_DE'
FROM OffToday o
INNER JOIN Employees e ON e.FullName = o.FullName AND e.IsActive = 1
WHERE NOT EXISTS (
    SELECT 1 FROM ShiftEntries se
    WHERE se.EmployeeId  = e.EmployeeId
      AND se.ShiftDate   = '2026-07-10'
      AND se.SourceSheet = N'GSD_DE'
);

PRINT N'OFF entries inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
PRINT N'Part 5 done.';
GO

-- ============================================================
-- PART 6: Night shift — Asal Wardaastiani Azar
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 6: Night shift — Asal Wardaastiani Azar...';

INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, ShiftStart, ShiftEnd, SourceSheet)
SELECT e.EmployeeId, '2026-07-10', N'Night', N'WORKING', N'22:00', N'07:00', N'GSD_DE'
FROM Employees e WHERE e.FullName = N'Asal Wardaastiani Azar' AND e.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM ShiftEntries se
      WHERE se.EmployeeId  = e.EmployeeId
        AND se.ShiftDate   = '2026-07-10'
        AND se.SourceSheet = N'GSD_DE'
  );

PRINT N'Part 6 done.';
GO

-- ============================================================
-- PART 7: Final report
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== ABSENCE SYNC RESULTS FOR 2026-07-10 ===';

SELECT
    N'SickLeave'   AS [Type],
    sl.EmployeeId,
    sl.FirstName + N' ' + sl.LastName          AS Name,
    CONVERT(NVARCHAR(10), sl.FirstDay, 104)     AS FirstDay,
    ISNULL(CONVERT(NVARCHAR(10), sl.LastDay, 104), N'OPEN') AS LastDay,
    sl.Comments
FROM SickLeaves sl
WHERE sl.FirstDay >= '2026-07-06'
  AND sl.EmployeeId IN (N'9074557', N'9122675')
UNION ALL
SELECT
    N'SickLeave', sl.EmployeeId,
    sl.FirstName + N' ' + sl.LastName,
    CONVERT(NVARCHAR(10), sl.FirstDay, 104),
    ISNULL(CONVERT(NVARCHAR(10), sl.LastDay, 104), N'OPEN'),
    sl.Comments
FROM SickLeaves sl
INNER JOIN Employees e ON e.EmployeeId = sl.EmployeeId
WHERE sl.FirstDay = '2026-07-10'
  AND e.FullName IN (N'Annabela Scavo', N'John Daniel Wendland',
                     N'Sebastian H' + NCHAR(246) + N'ck')
ORDER BY [Type], FirstDay;

SELECT
    se.ShiftType    AS [Type],
    e.FullName      AS Name,
    CONVERT(NVARCHAR(10), se.ShiftDate, 104) AS [Date],
    se.RawValue     AS Note
FROM ShiftEntries se
INNER JOIN Employees e ON e.EmployeeId = se.EmployeeId
WHERE se.ShiftDate   = '2026-07-10'
  AND se.SourceSheet = N'GSD_DE'
  AND se.ShiftType   IN (N'AL', N'OFF', N'WORKING')
ORDER BY se.ShiftType, e.FullName;

PRINT N'';
PRINT N'OPEN ITEMS:';
PRINT N'  1. Yiting Qiang — EmployeeId=UNKNOWN_YQ; update once numeric ID confirmed from HR.';
PRINT N'  2. Pascal Dutz + Mark Bachmann — SL LastDay=NULL (extension open-ended); update when AU/return confirmed.';
PRINT N'  3. Annabela Scavo, John Daniel Wendland, Sebastian Höck — SL FirstDay set to today; verify actual start date.';
PRINT N'  4. 18 AL employees — ShiftEntry only (no Vacations period). Add Vacations records once full AL periods known.';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== Absence Sync committed — 2026-07-10 ===';
END
ELSE
    PRINT N'=== ERROR: Rolled back. No changes committed. ===';
GO
