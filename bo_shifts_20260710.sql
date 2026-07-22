-- ============================================================
-- Back Office Shift Import — 2026-07-10 (Friday)
-- Source: BO Liste (daily roster)
-- 21 employees — ShiftType = WORKING, SourceSheet = GSD_DE
--
-- Name corrections applied:
--   "Stefan Becker"   -> "Stephan Becker"  (DB canonical form)
--   "Kai Erik Kumlehn" -> "Kai Eric Kumlehn" (DB canonical form)
--
-- Annotations preserved in RawValue:
--   LEW, Avacon, Enviam, MDM  = customer/project assignment
--   Newjoiner                 = first day
--   BAG WIC                   = WIC duty (IsWicDuty = 1)
--   ab Montag WIC             = WIC transition from 2026-07-13; WicShiftEntry NOT created here
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== BO Shift Import 2026-07-10 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 1: Pre-check — confirm all 21 employees are in DB
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Employee lookup check...';

WITH BoList AS (
    SELECT * FROM (VALUES
        (N'Sina Sidharthan'),
        (N'Baschir Mahrufi'),
        (N'Javier Sang'),
        (N'Stephan Becker'),
        (N'Kai Eric Kumlehn'),
        (N'Lukas Schiefele'),
        (N'Anifa Ngcongo'),
        (N'Sebastian Lewandowski'),
        (N'Marko Bosnjak'),
        (N'Anisha Nellikka Panikkan'),
        (N'Dmytro Shelikhov'),
        (N'Erik Goecks'),
        (N'Ahmad Dabbas'),
        (N'Perim Rollin'),
        (N'Mohamad Nasir Amany'),
        (N'Hamyaz Pathan'),
        (N'Suhrab Sadieqy'),
        (N'Victoria Scholz'),
        (N'Mahboubeh Abdighara'),
        (N'Dennis Markus'),
        (N'Klaus Friedrich')
    ) AS t(FullName)
)
SELECT
    bl.FullName,
    CASE WHEN e.EmployeeId IS NOT NULL THEN N'OK — ' + e.EmployeeId ELSE N'MISSING' END AS Status
FROM BoList bl
LEFT JOIN Employees e ON e.FullName = bl.FullName
ORDER BY Status, bl.FullName;

PRINT N'Part 1 done. Rows with MISSING need employee record first.';
GO

-- ============================================================
-- PART 2: Insert ShiftEntries for 2026-07-10
--   One row per employee.
--   Guard: skip if (EmployeeId, ShiftDate, SourceSheet) exists.
--   Guard: skip if EmployeeId cannot be resolved (NULL).
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Inserting BO shift entries for 2026-07-10...';

WITH BoData AS (
    SELECT * FROM (VALUES
    --  FullName (DB canonical)         Start    End       RawNote              IsWicDuty
        (N'Sina Sidharthan',            N'08:00', N'17:00', N'BO — LEW',              0),
        (N'Baschir Mahrufi',            N'07:00', N'16:00', N'BO',                    0),
        (N'Javier Sang',                N'08:00', N'17:00', N'BO',                    0),
        (N'Stephan Becker',             N'07:00', N'16:00', N'BO',                    0),  -- "Stefan Becker" on sheet
        (N'Kai Eric Kumlehn',           N'08:00', N'17:00', N'BO',                    0),  -- "Kai Erik Kumlehn" on sheet
        (N'Lukas Schiefele',            N'08:00', N'17:00', N'BO',                    0),
        (N'Anifa Ngcongo',              N'10:00', N'17:00', N'BO',                    0),
        (N'Sebastian Lewandowski',      N'08:00', N'17:00', N'BO',                    0),
        (N'Marko Bosnjak',              N'08:00', N'17:00', N'BO — Newjoiner',        0),
        (N'Anisha Nellikka Panikkan',   N'08:00', N'17:00', N'BO',                    0),
        (N'Dmytro Shelikhov',           N'08:00', N'17:00', N'BO — BAG WIC',          1),  -- on-site WIC duty
        (N'Erik Goecks',                N'08:00', N'17:00', N'BO',                    0),
        (N'Ahmad Dabbas',               N'08:00', N'17:00', N'BO — Avacon',           0),
        (N'Perim Rollin',               N'08:00', N'17:00', N'BO — Enviam',           0),
        (N'Mohamad Nasir Amany',        N'08:00', N'17:00', N'BO — LEW; ab Mo WIC',   0),  -- WIC starts 2026-07-13
        (N'Hamyaz Pathan',              N'08:00', N'17:00', N'BO',                    0),
        (N'Suhrab Sadieqy',             N'08:00', N'17:00', N'BO',                    0),
        (N'Victoria Scholz',            N'08:00', N'17:00', N'BO — MDM',              0),
        (N'Mahboubeh Abdighara',        N'08:00', N'17:00', N'BO',                    0),
        (N'Dennis Markus',              N'08:00', N'17:00', N'BO',                    0),
        (N'Klaus Friedrich',            N'08:00', N'17:00', N'BO',                    0)
    ) AS t(FullName, ShiftStart, ShiftEnd, RawNote, IsWicDuty)
)
INSERT INTO ShiftEntries (EmployeeId, ShiftDate, RawValue, ShiftType, ShiftStart, ShiftEnd, IsWicDuty, SourceSheet)
SELECT
    e.EmployeeId,
    '2026-07-10',
    bd.RawNote,
    N'WORKING',
    bd.ShiftStart,
    bd.ShiftEnd,
    bd.IsWicDuty,
    N'GSD_DE'
FROM BoData bd
INNER JOIN Employees e ON e.FullName = bd.FullName AND e.IsActive = 1
WHERE NOT EXISTS (
    SELECT 1 FROM ShiftEntries se
    WHERE se.EmployeeId  = e.EmployeeId
      AND se.ShiftDate   = '2026-07-10'
      AND se.SourceSheet = N'GSD_DE'
);

PRINT N'Inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' shift entries.';
PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: WIC duty note for Dmytro Shelikhov (BAG WIC)
--   Ensure a WicShiftEntry exists for today marking him on-site.
--   SupportLocation = 'BAG WIC' (adjust to the exact DisplayName
--   from WicLocations if the location exists under a different name).
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: Dmytro Shelikhov — WicShiftEntry for BAG WIC...';

INSERT INTO WicShiftEntries (EmployeeId, ShiftDate, DayOfWeek, SupportLocation, WorkingShift, IsOnSite, IsGSDDay, IsOffDay, Task)
SELECT
    e.EmployeeId,
    '2026-07-10',
    N'Fri',
    N'BAG WIC',
    N'08:00 - 17:00',
    1,    -- IsOnSite
    0,
    0,
    N'WIC'
FROM Employees e
WHERE e.FullName = N'Dmytro Shelikhov' AND e.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM WicShiftEntries ws
      WHERE ws.EmployeeId = e.EmployeeId
        AND ws.ShiftDate  = '2026-07-10'
  );

PRINT N'Part 3 done.';
GO

-- ============================================================
-- PART 4: WIC transition flag — Mohamad Nasir Amany
--   Starts WIC from Monday 2026-07-13.
--   This only records the transition note; the actual WicShiftEntries
--   for 2026-07-13+ should be created when the Monday BO/WIC list arrives.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 4: WIC transition note for Mohamad Nasir Amany...';
PRINT N'NOTE: Mohamad Nasir Amany moves to WIC duty from 2026-07-13 (Monday).';
PRINT N'      Create WicShiftEntries for 2026-07-13+ separately when WIC assignment is confirmed.';
PRINT N'Part 4 done.';
GO

-- ============================================================
-- PART 5: Confirmation
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== INSERTED SHIFTS ===';

SELECT
    e.FullName,
    se.ShiftStart + N' - ' + se.ShiftEnd AS Shift,
    se.RawValue                           AS Note,
    se.IsWicDuty
FROM ShiftEntries se
INNER JOIN Employees e ON e.EmployeeId = se.EmployeeId
WHERE se.ShiftDate   = '2026-07-10'
  AND se.SourceSheet = N'GSD_DE'
ORDER BY se.ShiftStart, e.FullName;

PRINT N'';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== BO Shift Import committed successfully — 2026-07-10 ===';
END
ELSE
    PRINT N'=== ERROR: Transaction was rolled back. No changes committed. ===';
GO
