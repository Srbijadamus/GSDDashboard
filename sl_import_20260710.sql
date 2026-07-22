-- ============================================================
-- Sick Leave Import — 2026-07-10
-- Source: SL tracking sheet (7 records, period 26-Jun-26 to 1-Jul-26)
-- DurationDays = calendar days (inclusive range)
--
-- AU status stored in Comments:
--   "should have electronic AU" = AU not yet received, expected electronically
--   "AU sent via email"         = AU received via email
--   "TL reported the SL"        = Team Lead notification only, no AU yet
--
-- 3 EmployeeIds not in previous syncs — handled in Part 1:
--   9122675  Mark Bachmann   (TL: Oliver Schleusen)
--   9107615  Dennis Markus   (TL: Delia Panaitescu)  -- new ID for known agent
--   9124697  Merlin Voss     (TL: Karlo Coric)       -- new ID for known agent
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== SL Import 2026-07-10 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 1: Upsert employees whose IDs appear here for the first time
--   Pattern: skip if EmployeeId OR FullName already exists.
--   Employees already in DB via prior syncs are skipped cleanly.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Ensure all 7 employees are in Employees table...';

WITH NewEmps AS (
    SELECT * FROM (VALUES
        (N'9074557', N'Pascal',  N'Dutz',     N'Pascal Dutz'),
        (N'9085123', N'Anil',    N'Bedzeti',   N'Anil Bedzeti'),
        (N'9122675', N'Mark',    N'Bachmann',  N'Mark Bachmann'),
        (N'9074563', N'Ralf',    N'Turski',    N'Ralf Turski'),
        (N'9107615', N'Dennis',  N'Markus',    N'Dennis Markus'),
        (N'9124697', N'Merlin',  N'Voss',      N'Merlin Voss'),
        (N'9090514', N'Sam Alisha', N'Metzner', N'Sam Alisha Metzner')
    ) AS t(EmpId, FName, LName, FulName)
)
INSERT INTO Employees (EmployeeId, FirstName, LastName, FullName, IsActive, SourceSheet, CreatedAt)
SELECT ne.EmpId, ne.FName, ne.LName, ne.FulName, 1, N'GSD_DE', GETDATE()
FROM NewEmps ne
WHERE NOT EXISTS (
    SELECT 1 FROM Employees e
    WHERE e.EmployeeId = ne.EmpId OR e.FullName = ne.FulName
);

PRINT N'Inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' new employee rows.';

-- Warn about FullName matches under different EmployeeId (requires manual check)
WITH NewEmps AS (
    SELECT * FROM (VALUES
        (N'9122675', N'Mark Bachmann'),
        (N'9107615', N'Dennis Markus'),
        (N'9124697', N'Merlin Voss')
    ) AS t(SheetId, SheetName)
)
SELECT
    ne.SheetId   AS Sheet_EmployeeId,
    ne.SheetName,
    e.EmployeeId AS DB_EmployeeId,
    N'WARN — same FullName, different EmployeeId. SickLeave will use Sheet ID.' AS Note
FROM NewEmps ne
INNER JOIN Employees e ON e.FullName = ne.SheetName
WHERE e.EmployeeId <> ne.SheetId;

PRINT N'Part 1 done.';
GO

-- ============================================================
-- PART 2: Update TeamLeadName on Employees where not yet set
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Update TeamLeadName from SL data...';

UPDATE Employees SET TeamLeadName = N'Delia Panaitescu'
WHERE EmployeeId = N'9074557' AND (TeamLeadName IS NULL OR TeamLeadName = N'');

UPDATE Employees SET TeamLeadName = N'Karlo Coric'
WHERE EmployeeId = N'9085123' AND (TeamLeadName IS NULL OR TeamLeadName = N'');

UPDATE Employees SET TeamLeadName = N'Oliver Schleusen'
WHERE EmployeeId = N'9122675' AND (TeamLeadName IS NULL OR TeamLeadName = N'');

UPDATE Employees SET TeamLeadName = N'Karlo Coric'
WHERE EmployeeId = N'9074563' AND (TeamLeadName IS NULL OR TeamLeadName = N'');

UPDATE Employees SET TeamLeadName = N'Delia Panaitescu'
WHERE EmployeeId = N'9107615' AND (TeamLeadName IS NULL OR TeamLeadName = N'');

UPDATE Employees SET TeamLeadName = N'Karlo Coric'
WHERE EmployeeId = N'9124697' AND (TeamLeadName IS NULL OR TeamLeadName = N'');

UPDATE Employees SET TeamLeadName = N'Delia Panaitescu'
WHERE EmployeeId = N'9090514' AND (TeamLeadName IS NULL OR TeamLeadName = N'');

PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: Insert SickLeaves
--   Guard: UNIQUE (EmployeeId, FirstDay) — skip if exists.
--   DurationDays = calendar days inclusive (source data).
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: Inserting sick leave records...';

-- 1. Pascal Dutz — 26-Jun to 3-Jul (8 days) — pending electronic AU
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT N'9074557', N'Pascal', N'Dutz', N'Delia Panaitescu',
       '2026-06-26', '2026-07-03', 8, N'Self',
       N'Should have electronic AU — imported 2026-07-10', N'GSD_DE', GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId = N'9074557' AND FirstDay = '2026-06-26');

-- 2. Anil Bedzeti — 26-Jun to 3-Jul (8 days) — AU via email
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT N'9085123', N'Anil', N'Bedzeti', N'Karlo Coric',
       '2026-06-26', '2026-07-03', 8, N'Self',
       N'AU sent via email — imported 2026-07-10', N'GSD_DE', GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId = N'9085123' AND FirstDay = '2026-06-26');

-- 3. Mark Bachmann — 26-Jun to 3-Jul (8 days) — TL reported
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT N'9122675', N'Mark', N'Bachmann', N'Oliver Schleusen',
       '2026-06-26', '2026-07-03', 8, N'Self',
       N'TL reported the SL — imported 2026-07-10', N'GSD_DE', GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId = N'9122675' AND FirstDay = '2026-06-26');

-- 4. Ralf Turski — 26-Jun to 3-Jul (8 days) — TL reported
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT N'9074563', N'Ralf', N'Turski', N'Karlo Coric',
       '2026-06-26', '2026-07-03', 8, N'Self',
       N'TL reported the SL — imported 2026-07-10', N'GSD_DE', GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId = N'9074563' AND FirstDay = '2026-06-26');

-- 5. Dennis Markus — 29-Jun to 30-Jun (2 days) — AU via email
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT N'9107615', N'Dennis', N'Markus', N'Delia Panaitescu',
       '2026-06-29', '2026-06-30', 2, N'Self',
       N'AU sent via email — imported 2026-07-10', N'GSD_DE', GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId = N'9107615' AND FirstDay = '2026-06-29');

-- 6. Merlin Voss — 29-Jun to 3-Jul (5 days) — TL reported
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT N'9124697', N'Merlin', N'Voss', N'Karlo Coric',
       '2026-06-29', '2026-07-03', 5, N'Self',
       N'TL reported the SL — imported 2026-07-10', N'GSD_DE', GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId = N'9124697' AND FirstDay = '2026-06-29');

-- 7. Sam Metzner — 1-Jul only (1 day) — AU via email covering 26-Jun to 3-Jul
--    Note: AU certificate spans 26.06–03.07; this entry covers 01-Jul only.
--    Check whether 26-Jun entry already exists separately.
INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT N'9090514', N'Sam Alisha', N'Metzner', N'Delia Panaitescu',
       '2026-07-01', '2026-07-01', 1, N'Self',
       N'AU sent via email — AU certificate covers 26.06–03.07 — imported 2026-07-10', N'GSD_DE', GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM SickLeaves WHERE EmployeeId = N'9090514' AND FirstDay = '2026-07-01');

PRINT N'Inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' sick leave record(s) in last batch.';
PRINT N'Part 3 done.';
GO

-- ============================================================
-- PART 4: Flag — Dennis Markus EmployeeId cross-check
--   Previous SL (May 2026) was inserted by FullName lookup.
--   If that row used a different EmployeeId, it needs manual merge.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 4: Dennis Markus — prior SL cross-check...';

SELECT
    sl.Id,
    sl.EmployeeId,
    CONVERT(NVARCHAR(10), sl.FirstDay, 120) AS FirstDay,
    CONVERT(NVARCHAR(10), sl.LastDay,  120) AS LastDay,
    sl.DurationDays,
    sl.Comments,
    CASE WHEN sl.EmployeeId = N'9107615' THEN N'OK' ELSE N'WARN — different EmployeeId; verify' END AS IdCheck
FROM SickLeaves sl
WHERE sl.FirstName = N'Dennis' AND sl.LastName = N'Markus'
ORDER BY sl.FirstDay;

PRINT N'Part 4 done.';
GO

-- ============================================================
-- PART 5: Confirmation
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== IMPORTED SL RECORDS ===';

SELECT
    sl.Id,
    sl.EmployeeId,
    sl.FirstName + N' ' + sl.LastName       AS Employee,
    sl.TeamLeadName                          AS TL,
    CONVERT(NVARCHAR(10), sl.FirstDay, 120)  AS FirstDay,
    CONVERT(NVARCHAR(10), sl.LastDay,  120)  AS LastDay,
    sl.DurationDays                          AS Days,
    sl.Comments
FROM SickLeaves sl
WHERE sl.EmployeeId IN (N'9074557', N'9085123', N'9122675', N'9074563',
                        N'9107615', N'9124697', N'9090514')
  AND sl.FirstDay >= '2026-06-26'
ORDER BY sl.FirstDay, sl.LastName;

PRINT N'';
PRINT N'REMINDER: Sam Metzner AU covers 26.06-03.07 but only 01-Jul logged here.';
PRINT N'          Check if 26-Jun entry exists or needs to be added separately.';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== SL Import committed successfully — 2026-07-10 ===';
END
ELSE
    PRINT N'=== ERROR: Transaction was rolled back. No changes committed. ===';
GO
