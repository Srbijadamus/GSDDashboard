using GSDDashboard.API.Data;
using GSDDashboard.API.Modules.ALCalendar;
using GSDDashboard.API.Modules.Overview;
using GSDDashboard.API.Modules.Overview;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.Dashboard;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Modules.Vwic;
using GSDDashboard.API.Modules.Breaks;
using GSDDashboard.API.Modules.SickLeave;
using GSDDashboard.API.Modules.Vacations;
using GSDDashboard.API.Modules.PublicHolidays;
using GSDDashboard.API.Modules.Pipeline;
using GSDDashboard.API.Modules.WicSchedule;
using GSDDashboard.API.Modules.Training;
using GSDDashboard.API.Services;
using GSDDashboard.API.Modules.Employees;
using GSDDashboard.API.Modules.ALBalance;
using GSDDashboard.API.Modules.Attendance;
using GSDDashboard.API.Modules.Backup;
using GSDDashboard.API.Modules.SubstituteAccept;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GSDContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(3)
    ));

builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddScoped<WicShiftService>();
builder.Services.AddScoped<WicCardsService>();
builder.Services.AddScoped<VwicService>();
builder.Services.AddScoped<BreakService>();
builder.Services.AddScoped<SickLeaveService>();
builder.Services.AddScoped<VacationService>();
builder.Services.AddScoped<ShiftSyncService>();
builder.Services.AddScoped<PublicHolidayService>();
builder.Services.AddScoped<ShiftValidationService>();
builder.Services.AddScoped<PipelineService>();
builder.Services.AddScoped<WicScheduleService>();
builder.Services.AddScoped<TrainingService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<ALBalanceService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<GSDDashboard.API.Modules.ALCalendar.ALCalendarService>();
builder.Services.AddScoped<GSDDashboard.API.Modules.Overview.OverviewService>();
builder.Services.AddSingleton<ReachabilityService>();
builder.Services.AddScoped<CoverageEvaluator>();
builder.Services.AddScoped<SubstitutionService>();
builder.Services.AddScoped<ForecastService>();
builder.Services.AddScoped<WhatIfService>();
builder.Services.AddScoped<BriefingService>();
builder.Services.AddScoped<ALPlanningService>();
builder.Services.AddScoped<WicCoverageService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "EON GSD Dashboard API", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "GSD Dashboard API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseDefaultFiles();

// Ensure BreakSlots table exists (idempotent – safe on every startup)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BreakSlots')
        CREATE TABLE BreakSlots (
            Id             INT            IDENTITY(1,1) PRIMARY KEY,
            EmployeeId     NVARCHAR(20)   NOT NULL,
            BreakDate      DATE           NOT NULL,
            BreakStart     NVARCHAR(10)   NOT NULL,
            BreakEnd       NVARCHAR(10)   NOT NULL,
            ActualStart    NVARCHAR(10)   NULL,
            ActualEnd      NVARCHAR(10)   NULL,
            DurationMinutes INT           NOT NULL DEFAULT 30,
            Status         NVARCHAR(20)   NOT NULL DEFAULT 'SCHEDULED',
            AgentRole      NVARCHAR(20)   NULL
        )
    """);
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Break_Date')   CREATE INDEX IX_Break_Date    ON BreakSlots (BreakDate)");
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Break_Status') CREATE INDEX IX_Break_Status  ON BreakSlots (Status)");
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Break_EmpDate') CREATE INDEX IX_Break_EmpDate ON BreakSlots (EmployeeId, BreakDate)");
}

// Ensure VwicRotationSlots table exists (idempotent)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VwicRotationSlots')
        CREATE TABLE VwicRotationSlots (
            Id           INT          IDENTITY(1,1) PRIMARY KEY,
            EmployeeId   NVARCHAR(20) NOT NULL,
            RotationDate DATE         NOT NULL,
            SlotStart    NVARCHAR(5)  NOT NULL,
            SlotEnd      NVARCHAR(5)  NOT NULL
        )
    """);
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_VwicRot_Date')    CREATE INDEX IX_VwicRot_Date    ON VwicRotationSlots (RotationDate)");
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_VwicRot_EmpDate') CREATE INDEX IX_VwicRot_EmpDate ON VwicRotationSlots (EmployeeId, RotationDate)");
}

// WIC Coverage — add columns to Employees and WicLocations, create AgentReachableCities (all idempotent)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Employees') AND name='PrimaryKid')   ALTER TABLE Employees ADD PrimaryKid   nvarchar(20)  NULL
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Employees') AND name='SecondaryKid') ALTER TABLE Employees ADD SecondaryKid nvarchar(20)  NULL
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Employees') AND name='InfosysEmail') ALTER TABLE Employees ADD InfosysEmail nvarchar(200) NULL
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Employees') AND name='EonEmail')     ALTER TABLE Employees ADD EonEmail     nvarchar(200) NULL
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Employees') AND name='HasCar')       ALTER TABLE Employees ADD HasCar       bit           NULL
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Employees') AND name='GroupRegion')  ALTER TABLE Employees ADD GroupRegion  nvarchar(100) NULL
    """);
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('WicLocations') AND name='OpeningDay') ALTER TABLE WicLocations ADD OpeningDay nvarchar(200)  NULL
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('WicLocations') AND name='Comment')    ALTER TABLE WicLocations ADD Comment    nvarchar(1000) NULL
    """);
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AgentReachableCities')
        CREATE TABLE AgentReachableCities (
            Id           INT           IDENTITY(1,1) PRIMARY KEY,
            EmployeeId   NVARCHAR(20)  NULL,
            EmployeeName NVARCHAR(200) NOT NULL,
            City         NVARCHAR(200) NOT NULL,
            Source       NVARCHAR(20)  NOT NULL DEFAULT 'seed'
        )
    """);
}

// WIC Coverage — seed import (idempotent; skips if data already present)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
    await WicCoverageImport.RunAsync(db);
}

app.UseStaticFiles();

app.MapDashboardEndpoints();
app.MapShiftEndpoints();
app.MapWicEndpoints();
app.MapWicCardsEndpoints();
app.MapVwicDailyEndpoints();
app.MapAttendanceEndpoints();
app.MapSickLeaveEndpoints();
app.MapVacationEndpoints();
app.MapPublicHolidayEndpoints();
app.MapPipelineEndpoints();
app.MapWicScheduleEndpoints();
app.MapTrainingEndpoints();
app.MapALBalanceEndpoints();
app.MapEmployeeEndpoints();
app.MapShiftReorderEndpoints();
app.MapALCalendarEndpoints();
app.MapOverviewEndpoints();
app.MapBackupEndpoints();
app.MapReachabilityEndpoints();
app.MapSubstitutionEndpoints();
app.MapSubstituteAcceptEndpoints();
app.MapForecastEndpoints();
app.MapWhatIfEndpoints();
app.MapBriefingEndpoints();
app.MapALPlanningEndpoints();
app.MapBreakEndpoints();
app.MapWicCoverageEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));
app.MapFallbackToFile("index.html");
app.Run();












