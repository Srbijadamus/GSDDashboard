-- ============================================================
-- WIC Opening Hours Change — effective 2026-07-27
-- Source: Opening hours update — 6 EON nuclear WIC sites
-- Applied by: Nebojsa Stojnic
-- Script date: 2026-07-27
--
-- Changes:
--   Brokdorf         Mon-Thu 07:15-16:15, Fri 07:00-13:00
--   Emmerthal        Mon-Thu 07:00-16:00, Fri 07:00-13:30
--   Stade            Mon+Thu 09:00-15:00, Tue+Wed+Fri Closed
--   Stadland         Mon-Thu 07:30-16:30, Fri 07:30-14:00
--   Grafenrheinfeld  Mon-Fri 07:30-16:00 (unchanged pattern, time corrected)
--   Essenbach        Mon-Thu 07:00-12:00 / 12:30-16:00, Fri 07:00-12:00 / 12:30-13:30
--
-- DayOfWeek key: 1=Mon 2=Tue 3=Wed 4=Thu 5=Fri
-- Safe to re-run: all INSERT blocks guarded with NOT EXISTS.
-- Historical rows (EffectiveFrom IS NULL or earlier) are never touched.
-- ============================================================

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
PRINT N'=== WIC Opening Hours Change 2026-07-27 starting ===';
BEGIN TRAN;
GO

-- ============================================================
-- PART 0: Ensure versioning columns exist
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 0: Schema check...';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('WicOpeningHours') AND name='EffectiveFrom')
    ALTER TABLE WicOpeningHours ADD EffectiveFrom DATE NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('WicOpeningHours') AND name='ChangeNote')
    ALTER TABLE WicOpeningHours ADD ChangeNote NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_WicHours_Effective')
    CREATE INDEX IX_WicHours_Effective ON WicOpeningHours (LocationCode, DayOfWeek, EffectiveFrom);

PRINT N'Part 0 done.';
GO

-- ============================================================
-- PART 1: Validate all 6 locations exist
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 1: Validate WicLocations...';

DECLARE @missing NVARCHAR(200) = NULL;
SELECT @missing = COALESCE(@missing + N', ', N'') + v.name
FROM (VALUES (N'Brokdorf'), (N'Emmerthal'), (N'Stade'), (N'Stadland'), (N'Grafenrheinfeld'), (N'Essenbach')) v(name)
WHERE NOT EXISTS (SELECT 1 FROM WicLocations WHERE DisplayName = v.name);

IF @missing IS NOT NULL
BEGIN
    RAISERROR(N'ABORT: WicLocations rows missing: %s', 16, 1, @missing);
    ROLLBACK TRAN;
    RETURN;
END

PRINT N'Part 1: All 6 locations confirmed.';
GO

-- ============================================================
-- PART 2: Current state snapshot (informational)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 2: Current WicOpeningHours snapshot (before change)...';

SELECT
    wl.DisplayName,
    woh.DayOfWeek,
    woh.IsClosed,
    woh.OpenTime,
    woh.CloseTime,
    woh.OpenTime2,
    woh.CloseTime2,
    woh.RawSchedule,
    woh.EffectiveFrom,
    woh.ChangeNote
FROM WicOpeningHours woh
INNER JOIN WicLocations wl ON wl.LocationCode = woh.LocationCode
WHERE wl.DisplayName IN (N'Brokdorf', N'Emmerthal', N'Stade', N'Stadland', N'Grafenrheinfeld', N'Essenbach')
ORDER BY wl.DisplayName, woh.DayOfWeek, woh.EffectiveFrom;

PRINT N'Part 2 done.';
GO

-- ============================================================
-- PART 3: Brokdorf — Mon-Thu 07:15-16:15, Fri 07:00-13:00
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 3: Brokdorf...';

-- Mon-Thu: 07:15-16:15
DECLARE @day INT = 1;
WHILE @day <= 4
BEGIN
    INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, RawSchedule, EffectiveFrom, ChangeNote)
    SELECT wl.LocationCode, @day, 0, N'07:15', N'16:15', N'07:15-16:15', '2026-07-27',
           N'Opening hours update 2026-07-27 — Brokdorf Mon-Thu.'
    FROM WicLocations wl
    WHERE wl.DisplayName = N'Brokdorf'
      AND NOT EXISTS (
          SELECT 1 FROM WicOpeningHours e
          WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = @day AND e.EffectiveFrom = '2026-07-27');
    SET @day = @day + 1;
END

-- Fri: 07:00-13:00
INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, RawSchedule, EffectiveFrom, ChangeNote)
SELECT wl.LocationCode, 5, 0, N'07:00', N'13:00', N'07:00-13:00', '2026-07-27',
       N'Opening hours update 2026-07-27 — Brokdorf Fri.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Brokdorf'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = 5 AND e.EffectiveFrom = '2026-07-27');

PRINT N'Part 3 done.';
GO

-- ============================================================
-- PART 4: Emmerthal — Mon-Thu 07:00-16:00, Fri 07:00-13:30
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 4: Emmerthal...';

DECLARE @day INT = 1;
WHILE @day <= 4
BEGIN
    INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, RawSchedule, EffectiveFrom, ChangeNote)
    SELECT wl.LocationCode, @day, 0, N'07:00', N'16:00', N'07:00-16:00', '2026-07-27',
           N'Opening hours update 2026-07-27 — Emmerthal Mon-Thu.'
    FROM WicLocations wl
    WHERE wl.DisplayName = N'Emmerthal'
      AND NOT EXISTS (
          SELECT 1 FROM WicOpeningHours e
          WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = @day AND e.EffectiveFrom = '2026-07-27');
    SET @day = @day + 1;
END

INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, RawSchedule, EffectiveFrom, ChangeNote)
SELECT wl.LocationCode, 5, 0, N'07:00', N'13:30', N'07:00-13:30', '2026-07-27',
       N'Opening hours update 2026-07-27 — Emmerthal Fri.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Emmerthal'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = 5 AND e.EffectiveFrom = '2026-07-27');

PRINT N'Part 4 done.';
GO

-- ============================================================
-- PART 5: Stade — Mon+Thu 09:00-15:00, Tue+Wed+Fri Closed
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 5: Stade...';

-- Monday open
INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, RawSchedule, EffectiveFrom, ChangeNote)
SELECT wl.LocationCode, 1, 0, N'09:00', N'15:00', N'09:00-15:00', '2026-07-27',
       N'Opening hours update 2026-07-27 — Stade Mon.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Stade'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = 1 AND e.EffectiveFrom = '2026-07-27');

-- Tuesday closed
INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, RawSchedule, EffectiveFrom, ChangeNote)
SELECT wl.LocationCode, 2, 1, N'Closed', '2026-07-27',
       N'Opening hours update 2026-07-27 — Stade Tue closed.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Stade'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = 2 AND e.EffectiveFrom = '2026-07-27');

-- Wednesday closed
INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, RawSchedule, EffectiveFrom, ChangeNote)
SELECT wl.LocationCode, 3, 1, N'Closed', '2026-07-27',
       N'Opening hours update 2026-07-27 — Stade Wed closed.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Stade'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = 3 AND e.EffectiveFrom = '2026-07-27');

-- Thursday open
INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, RawSchedule, EffectiveFrom, ChangeNote)
SELECT wl.LocationCode, 4, 0, N'09:00', N'15:00', N'09:00-15:00', '2026-07-27',
       N'Opening hours update 2026-07-27 — Stade Thu.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Stade'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = 4 AND e.EffectiveFrom = '2026-07-27');

-- Friday closed
INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, RawSchedule, EffectiveFrom, ChangeNote)
SELECT wl.LocationCode, 5, 1, N'Closed', '2026-07-27',
       N'Opening hours update 2026-07-27 — Stade Fri closed.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Stade'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = 5 AND e.EffectiveFrom = '2026-07-27');

PRINT N'Part 5 done.';
GO

-- ============================================================
-- PART 6: Stadland — Mon-Thu 07:30-16:30, Fri 07:30-14:00
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 6: Stadland...';

DECLARE @day INT = 1;
WHILE @day <= 4
BEGIN
    INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, RawSchedule, EffectiveFrom, ChangeNote)
    SELECT wl.LocationCode, @day, 0, N'07:30', N'16:30', N'07:30-16:30', '2026-07-27',
           N'Opening hours update 2026-07-27 — Stadland Mon-Thu.'
    FROM WicLocations wl
    WHERE wl.DisplayName = N'Stadland'
      AND NOT EXISTS (
          SELECT 1 FROM WicOpeningHours e
          WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = @day AND e.EffectiveFrom = '2026-07-27');
    SET @day = @day + 1;
END

INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, RawSchedule, EffectiveFrom, ChangeNote)
SELECT wl.LocationCode, 5, 0, N'07:30', N'14:00', N'07:30-14:00', '2026-07-27',
       N'Opening hours update 2026-07-27 — Stadland Fri.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Stadland'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = 5 AND e.EffectiveFrom = '2026-07-27');

PRINT N'Part 6 done.';
GO

-- ============================================================
-- PART 7: Grafenrheinfeld — Mon-Fri 07:30-16:00
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 7: Grafenrheinfeld...';

DECLARE @day INT = 1;
WHILE @day <= 5
BEGIN
    INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, RawSchedule, EffectiveFrom, ChangeNote)
    SELECT wl.LocationCode, @day, 0, N'07:30', N'16:00', N'07:30-16:00', '2026-07-27',
           N'Opening hours update 2026-07-27 — Grafenrheinfeld Mon-Fri.'
    FROM WicLocations wl
    WHERE wl.DisplayName = N'Grafenrheinfeld'
      AND NOT EXISTS (
          SELECT 1 FROM WicOpeningHours e
          WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = @day AND e.EffectiveFrom = '2026-07-27');
    SET @day = @day + 1;
END

PRINT N'Part 7 done.';
GO

-- ============================================================
-- PART 8: Essenbach — Mon-Thu 07:00-12:00 / 12:30-16:00
--                     Fri    07:00-12:00 / 12:30-13:30
-- (Uses OpenTime2/CloseTime2 for the afternoon session)
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 8: Essenbach...';

-- Mon-Thu: 07:00-12:00 / 12:30-16:00
DECLARE @day INT = 1;
WHILE @day <= 4
BEGIN
    INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, OpenTime2, CloseTime2, RawSchedule, EffectiveFrom, ChangeNote)
    SELECT wl.LocationCode, @day, 0, N'07:00', N'12:00', N'12:30', N'16:00',
           N'07:00-12:00 / 12:30-16:00', '2026-07-27',
           N'Opening hours update 2026-07-27 — Essenbach Mon-Thu split.'
    FROM WicLocations wl
    WHERE wl.DisplayName = N'Essenbach'
      AND NOT EXISTS (
          SELECT 1 FROM WicOpeningHours e
          WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = @day AND e.EffectiveFrom = '2026-07-27');
    SET @day = @day + 1;
END

-- Fri: 07:00-12:00 / 12:30-13:30
INSERT INTO WicOpeningHours (LocationCode, DayOfWeek, IsClosed, OpenTime, CloseTime, OpenTime2, CloseTime2, RawSchedule, EffectiveFrom, ChangeNote)
SELECT wl.LocationCode, 5, 0, N'07:00', N'12:00', N'12:30', N'13:30',
       N'07:00-12:00 / 12:30-13:30', '2026-07-27',
       N'Opening hours update 2026-07-27 — Essenbach Fri split.'
FROM WicLocations wl
WHERE wl.DisplayName = N'Essenbach'
  AND NOT EXISTS (
      SELECT 1 FROM WicOpeningHours e
      WHERE e.LocationCode = wl.LocationCode AND e.DayOfWeek = 5 AND e.EffectiveFrom = '2026-07-27');

PRINT N'Part 8 done.';
GO

-- ============================================================
-- PART 9: Update WicLocations.OpeningDay descriptive text
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'Part 9: Update WicLocations.OpeningDay...';

UPDATE WicLocations SET OpeningDay = N'Mo-Do 07:15-16:15, Fr 07:00-13:00'
WHERE DisplayName = N'Brokdorf';

UPDATE WicLocations SET OpeningDay = N'Mo-Do 07:00-16:00, Fr 07:00-13:30'
WHERE DisplayName = N'Emmerthal';

UPDATE WicLocations SET OpeningDay = N'Mo. Do. 09:00-15:00'
WHERE DisplayName = N'Stade';

UPDATE WicLocations SET OpeningDay = N'Mo-Do 07:30-16:30, Fr 07:30-14:00'
WHERE DisplayName = N'Stadland';

UPDATE WicLocations SET OpeningDay = N'Mo-Fr 07:30-16:00'
WHERE DisplayName = N'Grafenrheinfeld';

UPDATE WicLocations SET OpeningDay = N'Mo-Do 07:00-12:00/12:30-16:00, Fr 07:00-12:00/12:30-13:30'
WHERE DisplayName = N'Essenbach';

PRINT N'Part 9 done.';
GO

-- ============================================================
-- PART 10: Confirmation — new rows for 2026-07-27
-- ============================================================
IF @@TRANCOUNT = 0 RETURN;
PRINT N'';
PRINT N'=== NEW ROWS INSERTED (EffectiveFrom = 2026-07-27) ===';

SELECT
    wl.DisplayName,
    woh.DayOfWeek,
    woh.IsClosed,
    woh.OpenTime,
    woh.CloseTime,
    woh.OpenTime2,
    woh.CloseTime2,
    woh.RawSchedule,
    CONVERT(NVARCHAR(10), woh.EffectiveFrom, 120) AS EffectiveFrom
FROM WicOpeningHours woh
INNER JOIN WicLocations wl ON wl.LocationCode = woh.LocationCode
WHERE wl.DisplayName IN (N'Brokdorf', N'Emmerthal', N'Stade', N'Stadland', N'Grafenrheinfeld', N'Essenbach')
  AND woh.EffectiveFrom = '2026-07-27'
ORDER BY wl.DisplayName, woh.DayOfWeek;

PRINT N'';
GO

-- ============================================================
-- COMMIT
-- ============================================================
IF @@TRANCOUNT > 0
BEGIN
    COMMIT TRAN;
    PRINT N'=== WIC Opening Hours Change 2026-07-27 committed successfully ===';
    PRINT N'Effective: 2026-07-27';
END
ELSE
    PRINT N'=== ERROR: Transaction was rolled back. No changes committed. ===';
GO
