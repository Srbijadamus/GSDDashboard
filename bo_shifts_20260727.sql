-- ============================================================
-- Back Office Shift Import — 2026-07-27 (Sunday)
-- Source: BO Liste (daily roster)
-- 17 employees — ShiftType = WORKING, SourceSheet = GSD_DE
--
-- Name corrections applied (same as 2026-07-10):
--   "Stefan Becker"    → "Stephan Becker"   (DB canonical)
--   "Kai Erik Kumlehn" → "Kai Eric Kumlehn"  (DB canonical)
--   "Victor Winter"    → "Viktor Winter"     (DB canonical)
--   "Duc Quy"          → "Duc Quy Huynh"     (DB canonical — verify if mismatch)
--   "Yun he"           → "Yun Hee Ho"        (DB canonical from WicCoverageImport)
--
-- Notes from source:
--   Marko Bosnjak     — Newjoiner
--   Perim Rollin      — ENVIAM
--   Duc Quy Huynh     — Enviam
--   Mohamad Nasir Amany — LEW
--   Francois Sicot    — BAG
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== BO Shift Import 2026-07-27 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 1: Pre-check — confirm all 17 employees are in DB
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Employee lookup check...';

WITH BoList AS (
    SELECT * FROM (VALUES
        (N'Sina Sidharthan'),
        (N'Stephan Becker'),
        (N'Kai Eric Kumlehn'),
        (N'Anifa Ngcongo'),
        (N'Marko Bosnjak'),
        (N'Perim Rollin'),
        (N'Anisha Nellikka Panikkan'),
        (N'Khaled Alali'),
        (N'Duc Quy Huynh'),
        (N'Mohamad Nasir Amany'),
        (N'Elaheh Ramzi'),
        (N'Viktor Winter'),
        (N'Amir Nassri'),
        (N'Yun Hee Ho'),
        (N'Francois Sicot'),
        (N'Krishnendu Das'),
        (N'Victoria Scholz')
    ) AS t(FullName)
)
SELECT
    bl.FullName,
    CASE WHEN e.EmployeeId IS NOT NULL THEN N'OK — ' + e.EmployeeId ELSE N'MISSING' END AS Status
FROM BoList bl
LEFT JOIN Employees e ON e.FullName = bl.FullName AND e.IsActive = 1
ORDER BY Status, bl.FullName;

PRINT N'Part 1 done. MISSING rows will be skipped in insert (no crash).';
GO

-- ============================================================
-- PART 2: Insert ShiftEntries for 2026-07-27
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Inserting BO shift entries for 2026-07-27...';

WITH BoData AS (
    SELECT * FROM (VALUES
    --  FullName (DB canonical)             Start     End       RawNote
        (N'Sina Sidharthan',            N'08:00', N'17:00', N'BO'),
        (N'Stephan Becker',             N'07:00', N'16:00', N'BO'),                 -- "Stefan Becker" on sheet
        (N'Kai Eric Kumlehn',           N'08:00', N'17:00', N'BO'),                 -- "Kai Erik Kumlehn" on sheet
        (N'Anifa Ngcongo',              N'10:00', N'17:00', N'BO'),
        (N'Marko Bosnjak',              N'08:00', N'17:00', N'BO — Newjoiner'),
        (N'Perim Rollin',               N'08:00', N'17:00', N'BO — Enviam'),
        (N'Anisha Nellikka Panikkan',   N'08:00', N'17:00', N'BO'),
        (N'Khaled Alali',               N'08:00', N'17:00', N'BO'),
        (N'Duc Quy Huynh',              N'09:00', N'16:00', N'BO — Enviam'),        -- "Duc Quy" on sheet
        (N'Mohamad Nasir Amany',        N'08:00', N'17:00', N'BO — LEW'),
        (N'Elaheh Ramzi',               N'08:00', N'17:00', N'BO'),
        (N'Viktor Winter',              N'08:00', N'17:00', N'BO'),                 -- "Victor Winter" on sheet
        (N'Amir Nassri',                N'08:00', N'17:00', N'BO'),
        (N'Yun Hee Ho',                 N'08:00', N'13:00', N'BO'),                 -- "Yun he" on sheet
        (N'Francois Sicot',             N'08:00', N'17:00', N'BO — BAG'),
        (N'Krishnendu Das',             N'08:00', N'17:00', N'BO'),
        (N'Victoria Scholz',            N'10:00', N'16:00', N'BO')
    ) AS t(FullName, ShiftStart, ShiftEnd, RawNote)
)
INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, ShiftStart, ShiftEnd, IsWicDuty, SourceSheet)
SELECT
    e.EmployeeId,
    '2026-07-27',
    bd.RawNote,
    N'WORKING',
    bd.ShiftStart,
    bd.ShiftEnd,
    0,
    N'GSD_DE'
FROM BoData bd
INNER JOIN Employees e ON e.FullName = bd.FullName AND e.IsActive = 1
WHERE NOT EXISTS (
    SELECT 1 FROM ShiftEntries se
    WHERE se.EmployeeId  = e.EmployeeId
      AND se.ShiftDate   = '2026-07-27'
      AND se.SourceSheet = N'GSD_DE');

PRINT N'Inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' shift entries.';
PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: Insert BoEntries for UI display (BO Liste page)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: Inserting BoEntries for 2026-07-27 (dashboard UI)...';

WITH BoData AS (
    SELECT * FROM (VALUES
    --  FullName (display)              Start     End       Note           SortOrder
        (N'Sina Sidharthan',            N'08:00', N'17:00', N'',              1),
        (N'Stephan Becker',             N'07:00', N'16:00', N'',              2),
        (N'Kai Eric Kumlehn',           N'08:00', N'17:00', N'',              3),
        (N'Anifa Ngcongo',              N'10:00', N'17:00', N'',              4),
        (N'Marko Bosnjak',              N'08:00', N'17:00', N'Newjoiner',     5),
        (N'Perim Rollin',               N'08:00', N'17:00', N'ENVIAM',        6),
        (N'Anisha Nellikka Panikkan',   N'08:00', N'17:00', N'',              7),
        (N'Khaled Alali',               N'08:00', N'17:00', N'',              8),
        (N'Duc Quy Huynh',              N'09:00', N'16:00', N'Enviam',        9),
        (N'Mohamad Nasir Amany',        N'08:00', N'17:00', N'LEW',          10),
        (N'Elaheh Ramzi',               N'08:00', N'17:00', N'',             11),
        (N'Viktor Winter',              N'08:00', N'17:00', N'',             12),
        (N'Amir Nassri',                N'08:00', N'17:00', N'',             13),
        (N'Yun Hee Ho',                 N'08:00', N'13:00', N'',             14),
        (N'Francois Sicot',             N'08:00', N'17:00', N'BAG',          15),
        (N'Krishnendu Das',             N'08:00', N'17:00', N'',             16),
        (N'Victoria Scholz',            N'10:00', N'16:00', N'',             17)
    ) AS t(EmployeeName, ShiftStart, ShiftEnd, Note, SortOrder)
)
INSERT INTO BoEntries (EntryDate, EmployeeName, ShiftStart, ShiftEnd, Note, SortOrder)
SELECT '2026-07-27', bd.EmployeeName, bd.ShiftStart, bd.ShiftEnd, bd.Note, bd.SortOrder
FROM BoData bd
WHERE NOT EXISTS (
    SELECT 1 FROM BoEntries be
    WHERE be.EntryDate    = '2026-07-27'
      AND be.EmployeeName = bd.EmployeeName);

PRINT N'BoEntries inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
PRINT N'Part 3 done.';
GO

-- ============================================================
-- PART 4: Confirmation
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== BO SHIFT IMPORT RESULTS FOR 2026-07-27 ===';

SELECT
    e.FullName,
    se.ShiftStart + N' - ' + se.ShiftEnd AS Shift,
    se.RawValue AS Note
FROM ShiftEntries se
INNER JOIN Employees e ON e.EmployeeId = se.EmployeeId
WHERE se.ShiftDate   = '2026-07-27'
  AND se.SourceSheet = N'GSD_DE'
  AND se.ShiftType   = N'WORKING'
ORDER BY se.ShiftStart, e.FullName;

SELECT
    be.EmployeeName,
    be.ShiftStart + N' - ' + be.ShiftEnd AS Shift,
    be.Note
FROM BoEntries be
WHERE be.EntryDate = '2026-07-27'
ORDER BY be.SortOrder;

PRINT N'';
PRINT N'OPEN ITEMS:';
PRINT N'  1. "Yun he" on sheet mapped to "Yun Hee Ho" — verify correct DB name (alt: "Yun Hee Oh").';
PRINT N'  2. "Victor Winter" on sheet mapped to "Viktor Winter" — confirm spelling.';
PRINT N'  3. "Duc Quy" on sheet mapped to "Duc Quy Huynh" — confirm full name.';
PRINT N'  4. Any MISSING from Part 1 lookup — add employee record then re-run.';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== BO Shift Import committed successfully — 2026-07-27 ===';
END
ELSE
    PRINT N'=== ERROR: Transaction was rolled back. No changes committed. ===';
GO
