-- Rollback for the PART B on-demand flag (owner decision 2026-10-16).
-- Restores Employees.IsOnDemand for exactly one row to its pre-change value.
-- Generated 20261016-081422. Run with:
--   sqlcmd -S localhost\SQLEXPRESS -E -d GSDDashboard -i "C:\GSDDashboard_secure\Tools\AlTrackerOverride20261016\Data\rollback-9128154-20261016-081422.sql"
SET XACT_ABORT ON;
BEGIN TRAN;
UPDATE Employees SET IsOnDemand = 0 WHERE EmployeeId = '9128154';
IF @@ROWCOUNT <> 1 BEGIN ROLLBACK; RAISERROR('rollback rowcount <> 1',16,1); END
ELSE COMMIT;
