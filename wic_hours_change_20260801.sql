-- ============================================================
-- WIC Opening Hours Change — effective 2026-08-01
-- Source: Email "Opening hours change"
-- Approved by: Ion Ciuceanu
-- Acknowledged by: Silvia Dimitrova
-- Applied by: Nebojsa Stojnic
-- Script date: 2026-07-10
--
-- Changes:
--   1. Dortmund WIC:  Mon-Fri -> Mon-Thu (Friday closed from 2026-08-01)
--   2. Rendsburg WIC: Mon only -> Mon 08:30-16:30 + Fri 08:30-16:30
--   3. Rendsburg WIC: Hamza Forrousso = MAIN, Viktor Winter = BACKUP (confirm/insert)
--
-- DayOfWeek key: 1=Mon 2=Tue 3=Wed 4=Thu 5=Fri 6=Sat 7=Sun
-- Safe to re-run: all INSERT blocks guarded with NOT EXISTS.
-- Historical rows (EffectiveFrom IS NULL) are never touched.
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== WIC Opening Hours Change 2026-08-01 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 0: Add versioning columns if not already present
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 0: Schema — add EffectiveFrom / ChangeNote if missing...';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('WicOpeningHours') AND name='EffectiveFrom')
    ALTER TABLE WicOpeningHours ADD EffectiveFrom DATE NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('WicOpeningHours') AND name='ChangeNote')
    ALTER TABLE WicOpeningHours ADD ChangeNote NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_WicHours_Effective')
    CREATE INDEX IX_WicHours_Effective ON WicOpeningHours (LocationCode, DayOfWeek, EffectiveFrom);

PRINT N'Part 0 done.';
GO

-- ============================================================
-- PART 1: Validate locations exist
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Validate WicLocations...';

IF NOT EXISTS (SELECT 1 FROM WicLocations WHERE DisplayName = N'Dortmund')
BEGIN
    RAISERROR(N'ABORT: WicLocations row for Dortmund not found.', 16, 1);
    ROLLBACK TRAN;
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM WicLocations WHERE DisplayName = N'Rendsburg')
BEGIN
    RAISERROR(N'ABORT: WicLocations row for Rendsburg not found.', 16, 1);
    ROLLBACK TRAN;
    RETURN;
END

PRINT N'Part 1: Both locations confirmed.';
GO

-- ============================================================
-- PART 2: Current state (informational — read inside transaction)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Current WicOpeningHours snapshot (before change)...';

SELECT
    wl.DisplayName,
    woh.Id,
    woh.DayOfWeek,
    woh.IsClosed,
    woh.OpenTime,
    woh.CloseTime,
    woh.EffectiveFrom,
    woh.ChangeNote
FROM WicOpeningHours woh
INNER JOIN WicLocations wl ON wl.LocationCode = woh.LocationCode
WHERE wl.DisplayName IN (N'Dortmund', N'Rendsburg')
ORDER BY wl.DisplayName, woh.DayOfWeek, woh.EffectiveFrom;

SELECT
    wl.DisplayName,
    waa.Id,
    waa.EmployeeName,
    waa.AssignmentType,
    waa.IsActive
FROM WicAgentAssignments waa
INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName IN (N'Dortmund', N'Rendsburg')
ORDER BY wl.DisplayName, waa.AssignmentType, waa.EmployeeName;

PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: Dortmund — close Friday from 2026-08-01
--   Current: Mon-Fri open
--   New:     Mon-Thu open, Fri closed
--   Historical rows (EffectiveFrom IS NULL) are preserved.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: Dortmund — insert Friday=closed row effective 2026-08-01...';

INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, EffectiveFrom, ChangeNote)
SELECT
    wl.LocationCode,
    5,          -- Friday
    1,          -- IsClosed = true
    '2026-08-01',
    N'Opening hours change — approved Ion Ciuceanu, ack. Silvia Dimitrova. Fri removed from schedule.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Dortmund'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode
        AND e.DayOfWeek = 5
        AND e.EffectiveFrom = '2026-08-01'
  );

PRINT N'Part 3 done.';
GO

-- ============================================================
-- PART 4: Rendsburg — add Mon+Fri with times from 2026-08-01
--   Current: Mon only (no specific times recorded)
--   New:     Mon 08:30-16:30 + Fri 08:30-16:30
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 4: Rendsburg — insert Mon+Fri rows effective 2026-08-01...';

-- Monday 08:30-16:30
INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, EffectiveFrom, ChangeNote)
SELECT
    wl.LocationCode,
    1,          -- Monday
    0,          -- open
    N'08:30',
    N'16:30',
    '2026-08-01',
    N'Opening hours change — approved Ion Ciuceanu, ack. Silvia Dimitrova. Mon hours formalised 08:30-16:30.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Rendsburg'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode
        AND e.DayOfWeek = 1
        AND e.EffectiveFrom = '2026-08-01'
  );

-- Friday 08:30-16:30 (new day)
INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, EffectiveFrom, ChangeNote)
SELECT
    wl.LocationCode,
    5,          -- Friday
    0,          -- open
    N'08:30',
    N'16:30',
    '2026-08-01',
    N'Opening hours change — approved Ion Ciuceanu, ack. Silvia Dimitrova. Fri added 08:30-16:30.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Rendsburg'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode
        AND e.DayOfWeek = 5
        AND e.EffectiveFrom = '2026-08-01'
  );

PRINT N'Part 4 done.';
GO

-- ============================================================
-- PART 5: WicLocations.OpeningDay — update descriptive field
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 5: Update WicLocations OpeningDay...';

UPDATE WicLocations
SET OpeningDay = N'Mo. Di. Mi. Do. (Fr. bis 2026-07-31)'
WHERE DisplayName = N'Dortmund'
  AND (OpeningDay IS NULL OR OpeningDay NOT LIKE N'%2026-07-31%');

UPDATE WicLocations
SET OpeningDay = N'Mo. Fr. 08:30-16:30 (ab 2026-08-01)'
WHERE DisplayName = N'Rendsburg'
  AND (OpeningDay IS NULL OR OpeningDay NOT LIKE N'%2026-08-01%');

PRINT N'Part 5 done.';
GO

-- ============================================================
-- PART 6: WicAgentAssignments — Rendsburg MAIN + BACKUP
--   Hamza Forrousso = MAIN (primary contact)
--   Viktor Winter   = BACKUP (backup contact)
--   Both likely seeded already; guards prevent duplicates.
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 6: Rendsburg agent assignments...';

-- Ensure Hamza Forrousso is active MAIN
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive, Notes)
SELECT
    wl.LocationCode,
    N'Hamza Forrousso',
    N'MAIN',
    1,
    N'Primary contact — confirmed opening hours change email 2026-07-10'
FROM WicLocations wl
WHERE wl.DisplayName = N'Rendsburg'
  AND NOT EXISTS (
      SELECT 1 FROM WicAgentAssignments e
      WHERE e.LocationCode = wl.LocationCode
        AND e.EmployeeName = N'Hamza Forrousso'
        AND e.AssignmentType = N'MAIN'
        AND e.IsActive = 1
  );

-- Ensure Viktor Winter is active BACKUP
INSERT INTO WicAgentAssignments (LocationCode, EmployeeName, AssignmentType, IsActive, Notes)
SELECT
    wl.LocationCode,
    N'Viktor Winter',
    N'BACKUP',
    1,
    N'Backup contact — confirmed opening hours change email 2026-07-10'
FROM WicLocations wl
WHERE wl.DisplayName = N'Rendsburg'
  AND NOT EXISTS (
      SELECT 1 FROM WicAgentAssignments e
      WHERE e.LocationCode = wl.LocationCode
        AND e.EmployeeName = N'Viktor Winter'
        AND e.AssignmentType = N'BACKUP'
        AND e.IsActive = 1
  );

PRINT N'Part 6 done.';
GO

-- ============================================================
-- PART 7: Confirmation — affected rows
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== AFFECTED RECORDS (new rows inserted) ===';

SELECT
    N'WicOpeningHours' AS [Table],
    woh.Id,
    wl.DisplayName,
    woh.DayOfWeek,
    woh.IsClosed,
    woh.OpenTime,
    woh.CloseTime,
    CONVERT(NVARCHAR(10), woh.EffectiveFrom, 120) AS EffectiveFrom,
    woh.ChangeNote
FROM WicOpeningHours woh
INNER JOIN WicLocations wl ON wl.LocationCode = woh.LocationCode
WHERE wl.DisplayName IN (N'Dortmund', N'Rendsburg')
  AND woh.EffectiveFrom = '2026-08-01'
ORDER BY wl.DisplayName, woh.DayOfWeek;

SELECT
    N'WicAgentAssignments' AS [Table],
    waa.Id,
    wl.DisplayName,
    waa.EmployeeName,
    waa.AssignmentType,
    waa.IsActive,
    waa.Notes
FROM WicAgentAssignments waa
INNER JOIN WicLocations wl ON wl.LocationCode = waa.LocationCode
WHERE wl.DisplayName = N'Rendsburg'
  AND waa.IsActive = 1
ORDER BY waa.AssignmentType, waa.EmployeeName;

PRINT N'';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== WIC Opening Hours Change committed successfully ===';
    PRINT N'Effective date: 2026-08-01';
    PRINT N'Source: Email "Opening hours change", approved Ion Ciuceanu, ack. Silvia Dimitrova';
END
ELSE
    PRINT N'=== ERROR: Transaction was rolled back. No changes committed. ===';
GO
