-- ============================================================
-- Annual Leave Import — 2026-07-10
-- Source: Approved leave requests
--
-- Rules enforced:
--   Duplicate = same (EmployeeId + FirstDay + LastDay)
--   Secondary duplicate = same (normalised FullName + FirstDay + LastDay)
--   Half-day (0.5): WorkDaysNet stored as 0, flagged in Comments
--                   (Vacations.WorkDaysNet is INT — no decimal support)
--   New employee IDs not in prior syncs are upserted in Part 1.
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== AL Import 2026-07-10 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 1: Upsert employees whose IDs appear for the first time
--   Negin Bazmi   9126886  — in AgentSeeds, no numeric ID yet
--   Olaf Wittenberg 9124144 — in AgentSeeds, no numeric ID yet
--   Holger Petzholdt 9125517 — in AgentSeeds, no numeric ID yet
--   Others (Becker/Rollin/Dinkelmann/Metzner) already in DB.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Ensure all 7 employees exist in Employees table...';

WITH NewEmps AS (
    SELECT * FROM (VALUES
        (N'9126886', N'Negin',   N'Bazmi',      N'Negin Bazmi'),
        (N'9124144', N'Olaf',    N'Wittenberg',  N'Olaf Wittenberg'),
        (N'9074582', N'Stephan', N'Becker',      N'Stephan Becker'),
        (N'9125517', N'Holger',  N'Petzholdt',   N'Holger Petzholdt'),
        (N'9074559', N'Perim',   N'Rollin',       N'Perim Rollin'),
        (N'3193177', N'Gunter',  N'Dinkelmann',  N'Gunter Dinkelmann'),
        (N'9090514', N'Sam Alisha', N'Metzner',  N'Sam Alisha Metzner')
    ) AS t(EmpId, FName, LName, FulName)
)
INSERT INTO Employees (EmployeeId, FirstName, LastName, FullName, IsActive, SourceSheet, CreatedAt)
SELECT ne.EmpId, ne.FName, ne.LName, ne.FulName, 1, N'GSD_DE', GETDATE()
FROM NewEmps ne
WHERE NOT EXISTS (
    SELECT 1 FROM Employees e
    WHERE e.EmployeeId = ne.EmpId OR e.FullName = ne.FulName
);

PRINT N'New employees inserted (if any): ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
PRINT N'Part 1 done.';
GO

-- ============================================================
-- PART 2: Stage all 7 records — classify PENDING / DUPLICATE
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Duplicate analysis...';

DECLARE @Stage TABLE (
    EmpId       NVARCHAR(20)  NOT NULL,
    LastName    NVARCHAR(100) NOT NULL,
    FirstName   NVARCHAR(100) NOT NULL,
    FullName    NVARCHAR(200) NOT NULL,
    FirstDay    DATE          NOT NULL,
    LastDay     DATE          NOT NULL,
    WorkNet     DECIMAL(3,1)  NOT NULL,
    MonthStart  DATE          NOT NULL,
    Notes       NVARCHAR(500) NULL,
    Status      NVARCHAR(20)  NOT NULL DEFAULT N'PENDING',
    DupReason   NVARCHAR(200) NULL,
    InsertedId  INT           NULL
);

INSERT INTO @Stage (EmpId, LastName, FirstName, FullName, FirstDay, LastDay, WorkNet, MonthStart, Notes)
VALUES
--   EmpId       Last            First       Full                    FirstDay     LastDay      Net  MonthStart    Notes
    (N'9126886', N'Bazmi',      N'Negin',   N'Negin Bazmi',        '2026-08-27','2026-08-27', 1.0,'2026-08-01', NULL),
    (N'9124144', N'Wittenberg', N'Olaf',    N'Olaf Wittenberg',    '2026-11-02','2026-11-06', 5.0,'2026-11-01', NULL),
    (N'9074582', N'Becker',     N'Stephan', N'Stephan Becker',     '2026-08-14','2026-08-14', 1.0,'2026-08-01', NULL),
    (N'9125517', N'Petzholdt',  N'Holger',  N'Holger Petzholdt',   '2026-07-27','2026-07-31', 5.0,'2026-07-01', NULL),
    (N'9074559', N'Rollin',     N'Perim',   N'Perim Rollin',       '2026-12-28','2026-12-31', 4.0,'2026-12-01', NULL),
    (N'3193177', N'Dinkelmann', N'Gunter',  N'Gunter Dinkelmann',  '2026-07-31','2026-08-05', 4.0,'2026-07-01', NULL),
    (N'9090514', N'Metzner',    N'Sam',     N'Sam Alisha Metzner', '2026-07-13','2026-07-13', 0.5,'2026-07-01', N'Half day (0.5 net work days)');

-- Primary duplicate check: same EmployeeId + FirstDay + LastDay
UPDATE s
SET s.Status    = N'DUPLICATE',
    s.DupReason = N'EID+FirstDay+LastDay match — existing Id=' + CAST(v.Id AS NVARCHAR(10))
FROM @Stage s
INNER JOIN Vacations v
    ON  v.EmployeeId = s.EmpId
    AND v.FirstDay   = s.FirstDay
    AND v.LastDay    = s.LastDay
WHERE s.Status = N'PENDING';

-- Secondary duplicate check: normalised FullName + FirstDay + LastDay
-- (catches same person recorded under a different EmployeeId)
UPDATE s
SET s.Status    = N'DUPLICATE',
    s.DupReason = N'Name+FirstDay+LastDay match (EID diff) — existing Id=' + CAST(v.Id AS NVARCHAR(10))
FROM @Stage s
INNER JOIN Vacations v
    ON  LTRIM(RTRIM(LOWER(ISNULL(v.FirstName,'') + ' ' + ISNULL(v.LastName,'')))) =
        LTRIM(RTRIM(LOWER(s.FirstName + ' ' + s.LastName)))
    AND v.FirstDay = s.FirstDay
    AND v.LastDay  = s.LastDay
WHERE s.Status = N'PENDING';

-- Error check: employee not resolvable in Employees table
UPDATE s
SET s.Status    = N'ERROR',
    s.DupReason = N'EmployeeId ' + s.EmpId + N' not found in Employees table'
FROM @Stage s
WHERE s.Status = N'PENDING'
  AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmpId);

PRINT N'Part 2 done. Staging complete:';
SELECT Status, COUNT(*) AS Count FROM @Stage GROUP BY Status;
GO

-- ============================================================
-- PART 3: Insert PENDING records only
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: Inserting approved leave records...';

DECLARE @Stage TABLE (
    EmpId       NVARCHAR(20)  NOT NULL,
    LastName    NVARCHAR(100) NOT NULL,
    FirstName   NVARCHAR(100) NOT NULL,
    FullName    NVARCHAR(200) NOT NULL,
    FirstDay    DATE          NOT NULL,
    LastDay     DATE          NOT NULL,
    WorkNet     DECIMAL(3,1)  NOT NULL,
    MonthStart  DATE          NOT NULL,
    Notes       NVARCHAR(500) NULL,
    Status      NVARCHAR(20)  NOT NULL DEFAULT N'PENDING',
    DupReason   NVARCHAR(200) NULL,
    InsertedId  INT           NULL
);

INSERT INTO @Stage (EmpId, LastName, FirstName, FullName, FirstDay, LastDay, WorkNet, MonthStart, Notes)
VALUES
    (N'9126886', N'Bazmi',      N'Negin',   N'Negin Bazmi',        '2026-08-27','2026-08-27', 1.0,'2026-08-01', NULL),
    (N'9124144', N'Wittenberg', N'Olaf',    N'Olaf Wittenberg',    '2026-11-02','2026-11-06', 5.0,'2026-11-01', NULL),
    (N'9074582', N'Becker',     N'Stephan', N'Stephan Becker',     '2026-08-14','2026-08-14', 1.0,'2026-08-01', NULL),
    (N'9125517', N'Petzholdt',  N'Holger',  N'Holger Petzholdt',   '2026-07-27','2026-07-31', 5.0,'2026-07-01', NULL),
    (N'9074559', N'Rollin',     N'Perim',   N'Perim Rollin',       '2026-12-28','2026-12-31', 4.0,'2026-12-01', NULL),
    (N'3193177', N'Dinkelmann', N'Gunter',  N'Gunter Dinkelmann',  '2026-07-31','2026-08-05', 4.0,'2026-07-01', NULL),
    (N'9090514', N'Metzner',    N'Sam',     N'Sam Alisha Metzner', '2026-07-13','2026-07-13', 0.5,'2026-07-01', N'Half day (0.5 net work days)');

-- Re-run duplicate flags (table variable is fresh per GO batch)
UPDATE s SET s.Status = N'DUPLICATE', s.DupReason = N'EID+FirstDay+LastDay match'
FROM @Stage s
INNER JOIN Vacations v ON v.EmployeeId=s.EmpId AND v.FirstDay=s.FirstDay AND v.LastDay=s.LastDay
WHERE s.Status = N'PENDING';

UPDATE s SET s.Status = N'DUPLICATE', s.DupReason = N'Name+FirstDay+LastDay match (EID diff)'
FROM @Stage s
INNER JOIN Vacations v
    ON LTRIM(RTRIM(LOWER(ISNULL(v.FirstName,'')+' '+ISNULL(v.LastName,'')))) =
       LTRIM(RTRIM(LOWER(s.FirstName+' '+s.LastName)))
   AND v.FirstDay=s.FirstDay AND v.LastDay=s.LastDay
WHERE s.Status = N'PENDING';

UPDATE s SET s.Status = N'ERROR', s.DupReason = N'EmployeeId not in Employees'
FROM @Stage s
WHERE s.Status = N'PENDING'
  AND NOT EXISTS (SELECT 1 FROM Employees e WHERE e.EmployeeId = s.EmpId);

-- Actual INSERT for PENDING records
INSERT INTO Vacations
    (EmployeeId, LastName, FirstName, FirstDay, LastDay, WorkDaysNet,
     ApprovedDenied, Comments, SourceSheet, MonthStart, SourceYear, IsOverhead, CreatedAt)
SELECT
    s.EmpId,
    s.LastName,
    s.FirstName,
    s.FirstDay,
    s.LastDay,
    CASE WHEN s.WorkNet < 1.0 THEN 0 ELSE CAST(s.WorkNet AS INT) END,
    N'APPROVED',
    ISNULL(s.Notes, N'Approved AL — imported 2026-07-10'),
    N'GSD_DE',
    s.MonthStart,
    2026,
    0,
    GETDATE()
FROM @Stage s
WHERE s.Status = N'PENDING';

PRINT N'Inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' vacation row(s).';

-- Update InsertedId for report
UPDATE s
SET s.InsertedId = v.Id
FROM @Stage s
INNER JOIN Vacations v ON v.EmployeeId=s.EmpId AND v.FirstDay=s.FirstDay AND v.LastDay=s.LastDay
WHERE s.Status = N'PENDING';

UPDATE @Stage SET Status = N'INSERTED' WHERE Status = N'PENDING';

-- ============================================================
-- PART 4: Report
-- ============================================================
PRINT N'';
PRINT N'========================================';
PRINT N'INSERTED:';
SELECT
    s.InsertedId    AS [DB Id],
    s.EmpId         AS [Employee ID],
    s.FullName      AS [Name],
    CONVERT(NVARCHAR(10), s.FirstDay, 104) AS [From],
    CONVERT(NVARCHAR(10), s.LastDay,  104) AS [To],
    s.WorkNet       AS [Net Days],
    ISNULL(s.Notes, N'—') AS [Note]
FROM @Stage s WHERE s.Status = N'INSERTED'
ORDER BY s.FirstDay;

PRINT N'';
PRINT N'SKIPPED - DUPLICATE:';
SELECT
    s.EmpId         AS [Employee ID],
    s.FullName      AS [Name],
    CONVERT(NVARCHAR(10), s.FirstDay, 104) AS [From],
    CONVERT(NVARCHAR(10), s.LastDay,  104) AS [To],
    s.WorkNet       AS [Net Days],
    s.DupReason     AS [Reason]
FROM @Stage s WHERE s.Status = N'DUPLICATE'
ORDER BY s.FirstDay;

PRINT N'';
PRINT N'ERRORS:';
SELECT
    s.EmpId         AS [Employee ID],
    s.FullName      AS [Name],
    CONVERT(NVARCHAR(10), s.FirstDay, 104) AS [From],
    CONVERT(NVARCHAR(10), s.LastDay,  104) AS [To],
    s.DupReason     AS [Error]
FROM @Stage s WHERE s.Status = N'ERROR'
ORDER BY s.FirstDay;

-- Final duplicate check
PRINT N'';
PRINT N'FINAL CHECK — post-insert duplicate scan:';
SELECT
    v.EmployeeId,
    v.FirstName + N' ' + v.LastName AS FullName,
    CONVERT(NVARCHAR(10), v.FirstDay, 104) AS FirstDay,
    CONVERT(NVARCHAR(10), v.LastDay,  104) AS LastDay,
    COUNT(*) AS Copies
FROM Vacations v
WHERE v.EmployeeId IN (N'9126886',N'9124144',N'9074582',N'9125517',
                       N'9074559',N'3193177', N'9090514')
  AND v.FirstDay >= '2026-07-13'
GROUP BY v.EmployeeId, v.FirstName, v.LastName, v.FirstDay, v.LastDay
HAVING COUNT(*) > 1;

IF @@ROWCOUNT = 0
    PRINT N'OK — no duplicates found after insert.';
ELSE
    PRINT N'WARNING — duplicates detected; investigate above rows.';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'';
    PRINT N'=== AL Import committed — 2026-07-10 ===';
END
ELSE
    PRINT N'=== ERROR: Transaction rolled back. No changes committed. ===';
GO
