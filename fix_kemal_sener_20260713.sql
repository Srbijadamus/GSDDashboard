-- Fix Kemal Sener — replace UNKNOWN_KS with confirmed data
-- EmployeeId: 9125526 | Student | Voice | TL: Oliver Schleusen

USE GSDDashboard;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

-- 1. Update Employees
UPDATE Employees
SET
    EmployeeId   = N'9125526',
    Email        = N'Kemal.Sener.external@eon.com',
    JobTitle     = N'Student',
    TeamName     = N'Voice',
    TeamLeadName = N'Oliver Schleusen',
    SourceSheet  = N'GSD_DE'
WHERE FullName = N'Kemal Sener'
  AND (EmployeeId = N'UNKNOWN_KS' OR EmployeeId IS NULL);

PRINT N'Employees updated: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

-- 2. Fix SickLeaves — was inserted with UNKNOWN_KS
UPDATE SickLeaves
SET EmployeeId = N'9125526'
WHERE EmployeeId = N'UNKNOWN_KS';

PRINT N'SickLeaves EmployeeId fixed: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

-- 3. Confirm
SELECT
    e.EmployeeId, e.FullName, e.Email, e.JobTitle, e.TeamLeadName,
    sl.FirstDay, sl.LastDay, sl.DurationDays, sl.Comments
FROM Employees e
LEFT JOIN SickLeaves sl ON sl.EmployeeId = e.EmployeeId
WHERE e.FullName = N'Kemal Sener';

COMMIT;
PRINT N'=== Kemal Sener fix committed ===';
GO
