-- ============================================================
-- Absence Import — generated 2026-07-10
-- Source: Outlook Email Analysis
-- Records:
--   1. Tim Nguyen          — Annual Leave (PENDING)  — 3 periods, 12 workdays total
--   2. Senthuran Shanmugalingam — Annual Leave (APPROVED) — 1 period, 9 workdays
--   3. Dennis Markus       — Sick Leave (REPORTED)   — 2026-05-05 to 2026-05-08
--
-- Tables: Vacations, SickLeaves
-- Safe to re-run: all INSERTs guarded with NOT EXISTS.
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== Absence Import 2026-07-10 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 1: Validate employees
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Employee lookups...';

SELECT N'Tim Nguyen'                   AS LookedUp, EmployeeId, FullName FROM Employees WHERE FullName = N'Tim Nguyen';
SELECT N'Senthuran Shanmugalingam'     AS LookedUp, EmployeeId, FullName FROM Employees WHERE FullName = N'Senthuran Shanmugalingam';
SELECT N'Dennis Markus'                AS LookedUp, EmployeeId, FullName FROM Employees WHERE FullName = N'Dennis Markus';

-- Warn (not abort) if Tim Nguyen is missing — could be a new hire not yet in Employees
IF NOT EXISTS (SELECT 1 FROM Employees WHERE FullName = N'Tim Nguyen')
    PRINT N'WARN: Tim Nguyen not found in Employees — EmployeeId will be NULL in Vacations rows.';

IF NOT EXISTS (SELECT 1 FROM Employees WHERE FullName = N'Senthuran Shanmugalingam')
BEGIN
    RAISERROR(N'ABORT: Senthuran Shanmugalingam not found in Employees.', 16, 1);
    ROLLBACK TRAN; RETURN;
END

IF NOT EXISTS (SELECT 1 FROM Employees WHERE FullName = N'Dennis Markus')
BEGIN
    RAISERROR(N'ABORT: Dennis Markus not found in Employees.', 16, 1);
    ROLLBACK TRAN; RETURN;
END

PRINT N'Part 1 done.';
GO

-- ============================================================
-- PART 2: Tim Nguyen — Annual Leave (PENDING)
--   Period 1: 2026-07-20 – 2026-07-24  (5 workdays)
--   Period 2: 2026-07-28 – 2026-07-31  (4 workdays)
--   Period 3: 2026-08-03 – 2026-08-05  (3 workdays)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Tim Nguyen — 3 vacation periods...';

-- Period 1
INSERT INTO Vacations (EmployeeId, LastName, FirstName, FirstDay, LastDay, WorkDaysNet, ApprovedDenied, Comments, SourceSheet, IsOverhead, CreatedAt)
SELECT
    (SELECT TOP 1 EmployeeId FROM Employees WHERE FullName = N'Tim Nguyen'),
    N'Nguyen', N'Tim',
    '2026-07-20', '2026-07-24',
    5,
    N'PENDING',
    N'Imported 2026-07-10 — source: Outlook Email Analysis',
    N'Agents',
    0,
    GETDATE()
WHERE NOT EXISTS (
    SELECT 1 FROM Vacations v
    WHERE v.FirstName = N'Tim' AND v.LastName = N'Nguyen'
      AND v.FirstDay = '2026-07-20'
);

-- Period 2
INSERT INTO Vacations (EmployeeId, LastName, FirstName, FirstDay, LastDay, WorkDaysNet, ApprovedDenied, Comments, SourceSheet, IsOverhead, CreatedAt)
SELECT
    (SELECT TOP 1 EmployeeId FROM Employees WHERE FullName = N'Tim Nguyen'),
    N'Nguyen', N'Tim',
    '2026-07-28', '2026-07-31',
    4,
    N'PENDING',
    N'Imported 2026-07-10 — source: Outlook Email Analysis',
    N'Agents',
    0,
    GETDATE()
WHERE NOT EXISTS (
    SELECT 1 FROM Vacations v
    WHERE v.FirstName = N'Tim' AND v.LastName = N'Nguyen'
      AND v.FirstDay = '2026-07-28'
);

-- Period 3
INSERT INTO Vacations (EmployeeId, LastName, FirstName, FirstDay, LastDay, WorkDaysNet, ApprovedDenied, Comments, SourceSheet, IsOverhead, CreatedAt)
SELECT
    (SELECT TOP 1 EmployeeId FROM Employees WHERE FullName = N'Tim Nguyen'),
    N'Nguyen', N'Tim',
    '2026-08-03', '2026-08-05',
    3,
    N'PENDING',
    N'Imported 2026-07-10 — source: Outlook Email Analysis',
    N'Agents',
    0,
    GETDATE()
WHERE NOT EXISTS (
    SELECT 1 FROM Vacations v
    WHERE v.FirstName = N'Tim' AND v.LastName = N'Nguyen'
      AND v.FirstDay = '2026-08-03'
);

PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: Senthuran Shanmugalingam — Annual Leave (APPROVED)
--   Period: 2026-08-07 – 2026-08-19  (9 workdays)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: Senthuran Shanmugalingam — vacation...';

INSERT INTO Vacations (EmployeeId, LastName, FirstName, FirstDay, LastDay, WorkDaysNet, ApprovedDenied, Comments, SourceSheet, IsOverhead, CreatedAt)
SELECT
    (SELECT TOP 1 EmployeeId FROM Employees WHERE FullName = N'Senthuran Shanmugalingam'),
    N'Shanmugalingam', N'Senthuran',
    '2026-08-07', '2026-08-19',
    9,
    N'APPROVED',
    N'Imported 2026-07-10 — source: Outlook Email Analysis',
    N'Agents',
    0,
    GETDATE()
WHERE NOT EXISTS (
    SELECT 1 FROM Vacations v
    WHERE v.EmployeeId = (SELECT TOP 1 EmployeeId FROM Employees WHERE FullName = N'Senthuran Shanmugalingam')
      AND v.FirstDay = '2026-08-07'
);

PRINT N'Part 3 done.';
GO

-- ============================================================
-- PART 4: Dennis Markus — Sick Leave (REPORTED)
--   Period: 2026-05-05 – 2026-05-08  (4 workdays: Tue–Fri)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 4: Dennis Markus — sick leave...';

INSERT INTO SickLeaves (EmployeeId, FirstName, LastName, TeamLeadName, FirstDay, LastDay, DurationDays, LeaveType, Comments, SourceSheet, CreatedAt)
SELECT
    (SELECT TOP 1 EmployeeId FROM Employees WHERE FullName = N'Dennis Markus'),
    N'Dennis',
    N'Markus',
    (SELECT TOP 1 TeamLeadName FROM Employees WHERE FullName = N'Dennis Markus'),
    '2026-05-05',
    '2026-05-08',
    4,
    N'Self',
    N'Imported 2026-07-10 — source: Outlook Email Analysis, status: Reported',
    N'Agents',
    GETDATE()
WHERE NOT EXISTS (
    SELECT 1 FROM SickLeaves sl
    WHERE sl.EmployeeId = (SELECT TOP 1 EmployeeId FROM Employees WHERE FullName = N'Dennis Markus')
      AND sl.FirstDay = '2026-05-05'
);

PRINT N'Part 4 done.';
GO

-- ============================================================
-- PART 5: Confirmation — show inserted rows
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== IMPORTED RECORDS ===';

SELECT
    N'Vacation' AS [Type],
    v.Id,
    v.FirstName + N' ' + v.LastName AS Employee,
    v.EmployeeId,
    CONVERT(NVARCHAR(10), v.FirstDay,  120) AS FirstDay,
    CONVERT(NVARCHAR(10), v.LastDay,   120) AS LastDay,
    v.WorkDaysNet,
    v.ApprovedDenied AS Status
FROM Vacations v
WHERE v.LastName IN (N'Nguyen', N'Shanmugalingam')
  AND v.FirstDay IN ('2026-07-20', '2026-07-28', '2026-08-03', '2026-08-07')
ORDER BY v.LastName, v.FirstDay;

SELECT
    N'SickLeave' AS [Type],
    sl.Id,
    sl.FirstName + N' ' + sl.LastName AS Employee,
    sl.EmployeeId,
    CONVERT(NVARCHAR(10), sl.FirstDay, 120) AS FirstDay,
    CONVERT(NVARCHAR(10), sl.LastDay,  120) AS LastDay,
    sl.DurationDays,
    sl.LeaveType AS Status
FROM SickLeaves sl
WHERE sl.FirstName = N'Dennis' AND sl.LastName = N'Markus'
  AND sl.FirstDay = '2026-05-05';

PRINT N'';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== Absence Import committed successfully ===';
END
ELSE
    PRINT N'=== ERROR: Transaction was rolled back. No changes committed. ===';
GO
