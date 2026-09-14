SET NOCOUNT ON;
BEGIN TRAN;

-- 1.2 Employee DEMO-001
IF NOT EXISTS (SELECT 1 FROM Employees WHERE EmployeeId = 'DEMO-001')
BEGIN
    INSERT INTO Employees (EmployeeId, FirstName, LastName, FullName, PrimaryRole, TeamLeadName, Engagement, IsActive, IsTrainee, NppQualified)
    VALUES ('DEMO-001', N'DEMO', N'Test Agent', N'DEMO Test Agent', 'WIC', 'Tobias Rossberg', 'Full Time', 1, 0, 0);
END

-- 1.3 ShiftEntries — working days only (Fri 11, Mon 14 - Thu 17), hours per Essen - BP1 opening hours
INSERT INTO ShiftEntries (EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, IsWicDuty, AgentTask, SourceModule)
VALUES
('DEMO-001','2026-09-11','WIC_DUTY','09:00','12:00',0,'Essen - BP1','DEMO'),
('DEMO-001','2026-09-14','WIC_DUTY','09:00','16:30',0,'Essen - BP1','DEMO'),
('DEMO-001','2026-09-15','WIC_DUTY','09:00','16:30',0,'Essen - BP1','DEMO'),
('DEMO-001','2026-09-16','WIC_DUTY','09:00','16:30',0,'Essen - BP1','DEMO'),
('DEMO-001','2026-09-17','WIC_DUTY','09:00','16:30',0,'Essen - BP1','DEMO');

-- WicShiftEntries — same 5 working days
INSERT INTO WicShiftEntries (EmployeeId, ShiftDate, DayOfWeek, SupportLocation, WorkingShift, IsOnSite, IsGSDDay, IsOffDay, Task, LocationCode)
VALUES
('DEMO-001','2026-09-11','Friday',   'Essen - BP1','09:00-12:00',1,0,0,'WIC', N'DE~45131~Essen~Brüsseler Platz 1'),
('DEMO-001','2026-09-14','Monday',   'Essen - BP1','09:00-16:30',1,0,0,'WIC', N'DE~45131~Essen~Brüsseler Platz 1'),
('DEMO-001','2026-09-15','Tuesday',  'Essen - BP1','09:00-16:30',1,0,0,'WIC', N'DE~45131~Essen~Brüsseler Platz 1'),
('DEMO-001','2026-09-16','Wednesday','Essen - BP1','09:00-16:30',1,0,0,'WIC', N'DE~45131~Essen~Brüsseler Platz 1'),
('DEMO-001','2026-09-17','Thursday', 'Essen - BP1','09:00-16:30',1,0,0,'WIC', N'DE~45131~Essen~Brüsseler Platz 1');

COMMIT;

SELECT 'Employees' AS TableName, COUNT(*) AS Rows FROM Employees WHERE EmployeeId = 'DEMO-001'
UNION ALL
SELECT 'ShiftEntries', COUNT(*) FROM ShiftEntries WHERE EmployeeId = 'DEMO-001'
UNION ALL
SELECT 'WicShiftEntries', COUNT(*) FROM WicShiftEntries WHERE EmployeeId = 'DEMO-001';
