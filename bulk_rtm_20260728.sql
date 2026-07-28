USE GSDDashboard;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'RtmEntries')
CREATE TABLE RtmEntries (
    Id          INT           IDENTITY(1,1) PRIMARY KEY,
    EntryDate   DATE          NOT NULL,
    EmployeeId  NVARCHAR(20)  NOT NULL,
    FullName    NVARCHAR(200) NULL,
    ShiftStart  NVARCHAR(10)  NOT NULL DEFAULT '08:00',
    ShiftEnd    NVARCHAR(10)  NOT NULL DEFAULT '17:00',
    Tag         NVARCHAR(500) NULL,
    SourceLine  NVARCHAR(500) NULL,
    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Rtm_Date' AND object_id=OBJECT_ID('RtmEntries'))
    CREATE INDEX IX_Rtm_Date ON RtmEntries (EntryDate);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Rtm_Emp' AND object_id=OBJECT_ID('RtmEntries'))
    CREATE INDEX IX_Rtm_Emp ON RtmEntries (EmployeeId);
