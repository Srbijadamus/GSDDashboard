-- =============================================
-- GSD Dashboard Database Schema
-- SQL Server Express 2022
-- Run this script once on a clean database
-- =============================================

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'GSDDashboard')
BEGIN
    CREATE DATABASE GSDDashboard;
END
GO

USE GSDDashboard;
GO

-- =============================================
-- TABLE: Employees
-- =============================================
IF OBJECT_ID('dbo.Employees', 'U') IS NULL
CREATE TABLE Employees (
    Id              INT             PRIMARY KEY IDENTITY(1,1),
    EmployeeId      NVARCHAR(20)    NOT NULL,
    FirstName       NVARCHAR(100)   NULL,
    LastName        NVARCHAR(100)   NULL,
    FullName        NVARCHAR(200)   NULL,
    Engagement      NVARCHAR(50)    NULL,
    -- 'Full Time', 'Part-Time', 'Student'
    PrimaryRole     NVARCHAR(50)    NULL,
    -- 'Voice','SSP','Chat','Dispatcher','VWIC','SME','Bulk PWs','Trainer','Booking Tool','Chat CRO'
    SecondaryRole   NVARCHAR(50)    NULL,
    TeamLeadName    NVARCHAR(200)   NULL,
    Category        NVARCHAR(50)    NULL,
    -- 'Voice','Chat','WIC','SSP','Students','Management','Training','Long Absence','Dutch'
    IsActive        BIT             NOT NULL DEFAULT 1,
    IsTrainee       BIT             NOT NULL DEFAULT 0,
    PlannedRole     NVARCHAR(50)    NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_Employees_EmployeeId UNIQUE (EmployeeId)
);

-- =============================================
-- TABLE: ShiftEntries
-- =============================================
IF OBJECT_ID('dbo.ShiftEntries', 'U') IS NULL
CREATE TABLE ShiftEntries (
    Id              INT             PRIMARY KEY IDENTITY(1,1),
    EmployeeId      NVARCHAR(20)    NOT NULL,
    ShiftDate       DATE            NOT NULL,
    RawValue        NVARCHAR(200)   NULL,
    -- Always store original value
    ShiftType       NVARCHAR(20)    NOT NULL,
    -- WORKING, WIC_DUTY, AL, HALF_AL, SL, UL, OFF, OFF_WEEKEND,
    -- PH, LPH, CD, OL, CO, TRAINING, RESIGNED, EMPTY
    ShiftStart      NVARCHAR(10)    NULL,
    -- "08:00" (stored as string, NOT TIME type)
    ShiftEnd        NVARCHAR(10)    NULL,
    -- "17:00"
    IsWicDuty       BIT             NOT NULL DEFAULT 0,
    -- 1 when RawValue contains "WIC" (use .Contains, not .Equals)
    SourceSheet     NVARCHAR(20)    NULL,
    -- 'GSD_DE', 'WIC', 'Management', 'Trainees'
    CONSTRAINT UQ_ShiftEntries_EmpDateSheet UNIQUE (EmployeeId, ShiftDate, SourceSheet)
);

CREATE INDEX IX_Shift_Date ON ShiftEntries (ShiftDate);
CREATE INDEX IX_Shift_Emp  ON ShiftEntries (EmployeeId);
CREATE INDEX IX_Shift_Type ON ShiftEntries (ShiftType);

-- =============================================
-- TABLE: WicShiftEntries
-- =============================================
IF OBJECT_ID('dbo.WicShiftEntries', 'U') IS NULL
CREATE TABLE WicShiftEntries (
    Id              INT             PRIMARY KEY IDENTITY(1,1),
    EmployeeId      NVARCHAR(20)    NOT NULL,
    ShiftDate       DATE            NOT NULL,
    DayOfWeek       NVARCHAR(10)    NULL,
    SupportLocation NVARCHAR(200)   NULL,
    -- WIC center name, 'Global Service Desk', ' / '
    WicOpeningHours NVARCHAR(50)    NULL,
    -- '09:00 - 17:00', 'Closed', ' / '
    WorkingShift    NVARCHAR(50)    NULL,
    -- '08:00 - 17:00', 'OFF', 'AL', 'SL', 'UL'
    IsOnSite        BIT             NOT NULL DEFAULT 0,
    -- 1 if SupportLocation is not ' / ', not 'Closed', not NULL
    IsGSDDay        BIT             NOT NULL DEFAULT 0,
    -- 1 if SupportLocation = 'Global Service Desk'
    IsOffDay        BIT             NOT NULL DEFAULT 0,
    -- 1 if WorkingShift IN ('OFF','AL','SL','UL','OFFWE')
    CONSTRAINT UQ_WicShift_EmpDate UNIQUE (EmployeeId, ShiftDate)
);

CREATE INDEX IX_Wic_Date     ON WicShiftEntries (ShiftDate);
CREATE INDEX IX_Wic_Emp      ON WicShiftEntries (EmployeeId);
CREATE INDEX IX_Wic_Location ON WicShiftEntries (SupportLocation);

-- =============================================
-- TABLE: WicLocations
-- =============================================
IF OBJECT_ID('dbo.WicLocations', 'U') IS NULL
CREATE TABLE WicLocations (
    Id              INT             PRIMARY KEY IDENTITY(1,1),
    LocationCode    NVARCHAR(50)    NOT NULL,
    -- e.g. 'DE_Salzgitter', 'NL_Denbosch'
    DisplayName     NVARCHAR(200)   NOT NULL,
    FullAddress     NVARCHAR(500)   NULL,
    PostalCode      NVARCHAR(20)    NULL,
    City            NVARCHAR(100)   NULL,
    Country         NVARCHAR(5)     NULL,
    -- 'DE', 'NL'
    OpeningSchedule NVARCHAR(200)   NULL,
    IsActive        BIT             NOT NULL DEFAULT 1,
    CONSTRAINT UQ_WicLocations_Code UNIQUE (LocationCode)
);

-- =============================================
-- TABLE: DailyAttendance
-- =============================================
IF OBJECT_ID('dbo.DailyAttendance', 'U') IS NULL
CREATE TABLE DailyAttendance (
    Id                  INT             PRIMARY KEY IDENTITY(1,1),
    LocationName        NVARCHAR(200)   NOT NULL,
    Country             NVARCHAR(5)     NULL,
    AttendanceDate      DATE            NOT NULL,
    RawValue            NVARCHAR(100)   NULL,
    AttendanceType      NVARCHAR(20)    NULL,
    -- 'ASSIGNED', 'WO', 'CLOSED', 'PH', 'EMPTY'
    AssignedEmployeeId  NVARCHAR(20)    NULL
);

CREATE INDEX IX_Att_Date     ON DailyAttendance (AttendanceDate);
CREATE INDEX IX_Att_Location ON DailyAttendance (LocationName);
CREATE INDEX IX_Att_Country  ON DailyAttendance (Country);

-- =============================================
-- TABLE: SickLeaves
-- =============================================
IF OBJECT_ID('dbo.SickLeaves', 'U') IS NULL
CREATE TABLE SickLeaves (
    Id              INT             PRIMARY KEY IDENTITY(1,1),
    EmployeeId      NVARCHAR(20)    NULL,
    FirstName       NVARCHAR(100)   NULL,
    LastName        NVARCHAR(100)   NULL,
    TeamLeadName    NVARCHAR(200)   NULL,
    FirstDay        DATE            NOT NULL,
    LastDay         DATE            NOT NULL,
    DurationDays    INT             NULL,
    LeaveType       NVARCHAR(10)    NULL,
    -- 'Self', 'Child'
    ChildName       NVARCHAR(200)   NULL,
    Comments        NVARCHAR(500)   NULL,
    SourceSheet     NVARCHAR(50)    NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_SickLeaves_EmpFirstDay UNIQUE (EmployeeId, FirstDay)
);

CREATE INDEX IX_SL_Emp   ON SickLeaves (EmployeeId);
CREATE INDEX IX_SL_Dates ON SickLeaves (FirstDay, LastDay);
CREATE INDEX IX_SL_TL    ON SickLeaves (TeamLeadName);

-- =============================================
-- TABLE: Vacations
-- =============================================
IF OBJECT_ID('dbo.Vacations', 'U') IS NULL
CREATE TABLE Vacations (
    Id              INT             PRIMARY KEY IDENTITY(1,1),
    EmployeeId      NVARCHAR(20)    NULL,
    LastName        NVARCHAR(100)   NULL,
    FirstName       NVARCHAR(100)   NULL,
    FirstDay        DATE            NOT NULL,
    LastDay         DATE            NOT NULL,
    WorkDaysNet     INT             NULL,
    Comments        NVARCHAR(500)   NULL,
    ApprovedDenied  NVARCHAR(20)    NULL,
    ApproverName    NVARCHAR(200)   NULL,
    ApproverDate    DATE            NULL,
    MonthStart      DATE            NULL,
    SourceYear      INT             NULL,
    SourceSheet     NVARCHAR(20)    NULL,
    -- 'Agents', 'Overhead'
    CreatedAt       DATETIME2       NOT NULL DEFAULT GETDATE()
);

CREATE INDEX IX_Vac_Emp   ON Vacations (EmployeeId);
CREATE INDEX IX_Vac_Dates ON Vacations (FirstDay, LastDay);

-- =============================================
-- TABLE: ALBalance
-- =============================================
IF OBJECT_ID('dbo.ALBalance', 'U') IS NULL
CREATE TABLE ALBalance (
    Id                  INT             PRIMARY KEY IDENTITY(1,1),
    EmployeeId          NVARCHAR(20)    NULL,
    EmployeeName        NVARCHAR(200)   NULL,
    EligibleDays        INT             NOT NULL DEFAULT 28,
    PlannedTakenAL      INT             NOT NULL DEFAULT 0,
    RemainingAL         INT             NOT NULL DEFAULT 0,
    CountSL             INT             NOT NULL DEFAULT 0,
    CountUL             INT             NOT NULL DEFAULT 0,
    CountWorkingSundays INT             NOT NULL DEFAULT 0,
    CountFreeSundays    INT             NOT NULL DEFAULT 0,
    LastUpdated         DATETIME2       NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_ALBalance_EmpId UNIQUE (EmployeeId)
);

-- =============================================
-- SEED: WicLocations (38 DE + 2 NL = 40 total)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM WicLocations)
BEGIN
    INSERT INTO WicLocations (LocationCode, DisplayName, FullAddress, PostalCode, City, Country, IsActive) VALUES
    -- Germany
    ('DE_Arnsberg',         'Arnsberg',             'Hellefelder Str. 8',               '59821', 'Arnsberg',             'DE', 1),
    ('DE_Augsburg',         'Augsburg',              'Schaezlerstr. 3',                  '86150', 'Augsburg',             'DE', 1),
    ('DE_Bamberg',          'Bamberg',               'Doktor-Robert-Pfleger-Str. 20',    '96052', 'Bamberg',              'DE', 1),
    ('DE_Berlin_Gauss',     'Berlin (Gaußstr.)',     'Gaußstr. 11',                      '10589', 'Berlin',               'DE', 1),
    ('DE_Berlin_Kopenick',  'Berlin (Köpenicker)',   'Köpenicker Str. 32',               '12355', 'Berlin',               'DE', 1),
    ('DE_Brokdorf',         'Brokdorf',              'Osterende',                        '25576', 'Brokdorf',             'DE', 1),
    ('DE_Demmin_Hanse',     'Demmin (Am Hanseufer)', 'Am Hanseufer 2',                   '17109', 'Demmin',               'DE', 1),
    ('DE_Demmin_Wold',      'Demmin (Woldeforster)', 'Woldeforster Str. 6',              '17109', 'Demmin',               'DE', 1),
    ('DE_Dortmund',         'Dortmund',              'Florianstraße 15-21',              '44139', 'Dortmund',             'DE', 1),
    ('DE_Emmerthal',        'Emmerthal',             'Kraftwerksgelände',                '31860', 'Emmerthal',            'DE', 1),
    ('DE_Essen_BP1',        'Essen (Brüsseler Pl.)', 'Brüsseler Platz 1',               '45131', 'Essen',                'DE', 1),
    ('DE_Essen_TK1',        'Essen (ThyssenKrupp)',  'ThyssenKrupp Allee 1',             '45143', 'Essen',                'DE', 1),
    ('DE_Essenbach',        'Essenbach',             'Dammstraße',                       '84051', 'Essenbach',            'DE', 1),
    ('DE_Furstenwalde',     'Fürstenwalde',          'Langewahler Str. 60',              '15517', 'Fürstenwalde',         'DE', 1),
    ('DE_Grafenrheinfeld',  'Grafenrheinfeld',       'Kraftwerkstraße',                  '97506', 'Grafenrheinfeld',      'DE', 1),
    ('DE_Halle',            'Halle (Saale)',          'Magdeburger Str. 51',              '06112', 'Halle (Saale)',         'DE', 1),
    ('DE_Hamburg',          'Hamburg',               'Normannenweg 9',                   '20537', 'Hamburg',              'DE', 1),
    ('DE_Hannover',         'Hannover',              'Ricklinger Stadtweg 123-127',      '30459', 'Hannover',             'DE', 1),
    ('DE_Helmstedt',        'Helmstedt',             'Schillerstr. 3',                   '38350', 'Helmstedt',            'DE', 1),
    ('DE_Landshut',         'Landshut',              'Kiem-Pauli Str. 2',                '84036', 'Landshut',             'DE', 1),
    ('DE_Markkleeberg',     'Markkleeberg',          'Friedrich-Ebert-Str. 26',          '04416', 'Markkleeberg',         'DE', 1),
    ('DE_Munchen',          'München',               'Arnulfstr. 203',                   '80634', 'München',              'DE', 1),
    ('DE_Mulheim',          'Mülheim',               'Moritzstr. 16-22',                 '45476', 'Mülheim',              'DE', 1),
    ('DE_Munster',          'Münster',               'Weseler Str. 480',                 '48163', 'Münster',              'DE', 1),
    ('DE_NeuIsenburg',      'Neu-Isenburg',          'Flughafenstr. 20',                 '63263', 'Neu-Isenburg',         'DE', 1),
    ('DE_Neuss',            'Neuss',                 'Collingstr. 2',                    '41460', 'Neuss',                'DE', 1),
    ('DE_Osnabruck',        'Osnabrück',             'Goethering 23-29',                 '49074', 'Osnabrück',            'DE', 1),
    ('DE_Potsdam',          'Potsdam',               'Am Kanal 2-3',                     '14467', 'Potsdam',              'DE', 1),
    ('DE_Quickborn',        'Quickborn',             'Schleswag-HeinGas-Platz 1',        '25451', 'Quickborn',            'DE', 1),
    ('DE_Recklinghausen',   'Recklinghausen',        'Bochumer Str. 2',                  '45661', 'Recklinghausen',       'DE', 1),
    ('DE_Saarbrucken',      'Saarbrücken',           'Heinrich-Böcking-Str. 10-14',      '66121', 'Saarbrücken',          'DE', 1),
    ('DE_Saffig',           'Saffig',                'Rauschermühle',                    '56648', 'Saffig',               'DE', 1),
    ('DE_Salzgitter',       'Salzgitter',            'Joachim-Campe-Straße 14',          '38226', 'Salzgitter',           'DE', 1),
    ('DE_Siegen',           'Siegen',                'Friedrichstr. 60',                 '57072', 'Siegen',               'DE', 1),
    ('DE_Stade',            'Stade',                 'Bassenflether Chaussee',            '21683', 'Stade',                'DE', 1),
    ('DE_Stadland',         'Stadland',              'Dedesdorfer Straße 2',             '26935', 'Stadland',             'DE', 1),
    ('DE_Trier',            'Trier',                 'Eurener Str. 33',                  '54294', 'Trier',                'DE', 1),
    ('DE_Wesel',            'Wesel',                 'Reeser Landstr. 41',               '46483', 'Wesel',                'DE', 1),
    -- Netherlands
    ('NL_Denbosch',         's-Hertogenbosch',       'Willemsplein 4',                   NULL,    's-Hertogenbosch',      'NL', 1),
    ('NL_Zwolle',           'Zwolle',                'Grote Voort 247',                  NULL,    'Zwolle',               'NL', 1);
END
GO

PRINT 'Schema created successfully. WicLocations seeded with 40 locations.';
GO
