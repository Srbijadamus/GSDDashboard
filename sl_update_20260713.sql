-- ============================================================
-- SL Update — 2026-07-13
-- Source: Outlook absence notifications (3 emails)
--
-- Kemal Sener    NEW    SL 09-Jul (AU, 1 day)       → INSERT
-- Merlin Voss    UPDATE SL 29-Jun–02-Jul (AU confirm) → CORRECT LastDay 03→02-Jul
-- Tim Nguyen     AL 20-Jul/28-Jul/03-Aug            → SKIP (already in DB)
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== SL Update 2026-07-13 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 1: Kemal Sener — ensure employee exists
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Kemal Sener — employee check...';

INSERT INTO Employees (EmployeeId, FirstName, LastName, FullName, IsActive, SourceSheet, CreatedAt)
SELECT N'UNKNOWN_KS', N'Kemal', N'Sener', N'Kemal Sener', 1, N'GSD_DE', GETDATE()
WHERE NOT EXISTS (
    SELECT 1 FROM Employees WHERE FullName = N'Kemal Sener'
);

IF @@ROWCOUNT > 0
    PRINT N'NOTE: Kemal Sener inserted with EmployeeId=UNKNOWN_KS — update once numeric ID confirmed.';
ELSE
    PRINT N'Kemal Sener already in Employees.';

PRINT N'Part 1 done.';
GO

-- ============================================================
-- PART 2: Kemal Sener — SL 09-Jul-2026 (1 day, AU submitted)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Kemal Sener — SL insert...';

INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT
    e.EmployeeId,
    N'Kemal', N'Sener',
    e.TeamLeadName,
    '2026-07-09', '2026-07-09', 1,
    N'Self',
    N'AU submitted — 1 day SL 09-Jul. Source: Outlook notification.',
    N'GSD_DE', GETDATE()
FROM Employees e
WHERE e.FullName = N'Kemal Sener'
  AND NOT EXISTS (
      SELECT 1 FROM SickLeaves sl
      WHERE sl.EmployeeId = e.EmployeeId
        AND sl.FirstDay   = '2026-07-09'
  );

PRINT N'Inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' SL row(s).';
PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: Merlin Voss — correct LastDay 03-Jul → 02-Jul
--   Our DB (sl_import_20260710): FirstDay=29-Jun, LastDay=03-Jul, 5 days
--   AU (Krankmeldung email [2]):  FirstDay=29-Jun, LastDay=02-Jul, 4 days
--   AU is authoritative → correct LastDay and DurationDays
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: Merlin Voss — SL correction...';

-- Show current state first
SELECT
    sl.Id,
    sl.EmployeeId,
    CONVERT(NVARCHAR(10), sl.FirstDay, 104) AS FirstDay,
    CONVERT(NVARCHAR(10), sl.LastDay,  104) AS LastDay_Before,
    sl.DurationDays AS Days_Before,
    sl.Comments
FROM SickLeaves sl
WHERE sl.EmployeeId = N'9124697'
  AND sl.FirstDay   = '2026-06-29';

UPDATE SickLeaves
SET
    LastDay      = '2026-07-02',
    DurationDays = 4,
    Comments     = ISNULL(Comments, N'') + N' | CORRECTED 2026-07-13: LastDay updated 03-Jul→02-Jul per AU (Krankmeldung email).'
WHERE EmployeeId = N'9124697'
  AND FirstDay   = '2026-06-29'
  AND LastDay    = '2026-07-03';

IF @@ROWCOUNT > 0
    PRINT N'Merlin Voss SL corrected: LastDay 03-Jul → 02-Jul, DurationDays 5 → 4.';
ELSE
    PRINT N'Merlin Voss: no row matched (already corrected or EmployeeId differs).';

PRINT N'Part 3 done.';
GO

-- ============================================================
-- PART 4: Tim Nguyen — skip check
--   3 AL periods (20-Jul, 28-Jul, 03-Aug) already imported
--   in absence_import_20260710.sql — no action needed.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 4: Tim Nguyen — duplicate check (should already exist)...';

SELECT
    v.Id,
    v.EmployeeId,
    CONVERT(NVARCHAR(10), v.FirstDay, 104) AS FirstDay,
    CONVERT(NVARCHAR(10), v.LastDay,  104) AS LastDay,
    v.ApprovedDenied,
    N'Already in DB — SKIP' AS Status
FROM Vacations v
INNER JOIN Employees e ON e.EmployeeId = v.EmployeeId
WHERE e.FullName = N'Tim Nguyen'
  AND v.FirstDay IN ('2026-07-20','2026-07-28','2026-08-03')
ORDER BY v.FirstDay;

IF @@ROWCOUNT = 0
    PRINT N'WARNING: Tim Nguyen AL periods NOT found in DB — run absence_import_20260710.sql first.';
ELSE
    PRINT N'Tim Nguyen AL confirmed in DB — no insert needed.';

PRINT N'Part 4 done.';
GO

-- ============================================================
-- PART 5: Confirmation report
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== UPDATE RESULTS ===';

SELECT
    sl.EmployeeId,
    sl.FirstName + N' ' + sl.LastName                   AS Employee,
    CONVERT(NVARCHAR(10), sl.FirstDay, 104)              AS FirstDay,
    ISNULL(CONVERT(NVARCHAR(10), sl.LastDay, 104), N'OPEN') AS LastDay,
    sl.DurationDays                                      AS Days,
    sl.LeaveType,
    sl.Comments
FROM SickLeaves sl
WHERE (sl.EmployeeId = N'9124697' AND sl.FirstDay = '2026-06-29')
   OR sl.EmployeeId IN (
        SELECT e.EmployeeId FROM Employees e WHERE e.FullName = N'Kemal Sener'
      )
ORDER BY sl.FirstDay;

PRINT N'';
PRINT N'OPEN: Kemal Sener — EmployeeId=UNKNOWN_KS if new. Get numeric ID from HR and update.';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== SL Update committed — 2026-07-13 ===';
END
ELSE
    PRINT N'=== ERROR: Rolled back. No changes committed. ===';
GO
