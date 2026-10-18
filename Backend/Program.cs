using GSDDashboard.API;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.ALCalendar;
using GSDDashboard.API.Modules.Overview;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.Dashboard;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Modules.Vwic;
using GSDDashboard.API.Modules.Breaks;
using GSDDashboard.API.Modules.SickLeave;
using GSDDashboard.API.Modules.Vacations;
using GSDDashboard.API.Modules.PublicHolidays;
using GSDDashboard.API.Modules.Pipeline;
using GSDDashboard.API.Modules.Rollout;
using GSDDashboard.API.Modules.WicSchedule;
using GSDDashboard.API.Modules.Training;
using GSDDashboard.API.Services;
using GSDDashboard.API.Modules.Employees;
using GSDDashboard.API.Modules.ALBalance;
using GSDDashboard.API.Modules.Attendance;
using GSDDashboard.API.Modules.Backup;
using GSDDashboard.API.Modules.SubstituteAccept;
using GSDDashboard.API.Modules.BoList;
using GSDDashboard.API.Modules.BulkRtm;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Modules.WicAssistant;
using GSDDashboard.API.Modules.Assistant;
using GSDDashboard.API.Modules.Admin;
using GSDDashboard.API.Modules.HrExport;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.MyShifts;
using GSDDashboard.API.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    // Non-committed local configuration (AppUserRoles:* seed). See .gitignore.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}
var runtimeConfigPath = PulseRuntimeConfiguration.Load(builder.Configuration);
Console.WriteLine($"Pulse runtime configuration loaded: {runtimeConfigPath}; service credential/device present; HTTP loopback upstream.");
if (builder.Configuration.GetValue<bool>("Pulse:ConfigPreflight"))
{
    Console.WriteLine($"Runtime configuration verified: {runtimeConfigPath}; service credential/device present; HTTP loopback upstream.");
    return;
}

builder.Services.AddDbContext<GSDContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(3)
    ));

builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddScoped<WicShiftService>();
builder.Services.AddScoped<WicCardsService>();
builder.Services.AddScoped<WicCoverageGapService>();
builder.Services.AddScoped<WicAssignCandidatesService>();
builder.Services.AddScoped<VwicService>();
builder.Services.AddScoped<BreakService>();
builder.Services.AddScoped<SickLeaveService>();
builder.Services.AddScoped<VacationService>();
builder.Services.AddScoped<ShiftSyncService>();
// One shared sickness-period rule for the modal, the active list, the absence feed and the Briefing.
builder.Services.AddScoped<SickLeavePeriodService>();
builder.Services.AddScoped<AvailabilityResolver>();
builder.Services.AddScoped<PublicHolidayService>();
builder.Services.AddScoped<ShiftValidationService>();
builder.Services.AddScoped<PipelineService>();
builder.Services.AddScoped<RolloutService>();
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
builder.Services.AddScoped<BoListService>();
builder.Services.AddScoped<BulkRtmService>();
builder.Services.AddScoped<RosterService>();
builder.Services.AddScoped<WicAssistantService>();
builder.Services.AddScoped<WicMigrationDryRunService>();
builder.Services.AddScoped<WicConflictDetector>();
builder.Services.AddScoped<DemoDataAdminService>();
builder.Services.AddScoped<HrExportService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient<IKioskProxy, KioskProxy>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// KID-only cookie login. SecurePolicy.SameAsRequest: the session cookie is Secure
// over the https devtunnel URL and plain on http://localhost:5000, so login works
// over BOTH (hard requirement — see documentation/auth_rbac.md).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name         = builder.Configuration["Auth:CookieName"] ?? "gsd_auth";
        options.Cookie.HttpOnly     = true;
        options.Cookie.SameSite     = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration   = true;
        options.ExpireTimeSpan      = TimeSpan.FromHours(12);
        // API, not MVC: never redirect to a login page — answer with status codes.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.Configure<FeaturesOptions>(builder.Configuration.GetSection("Features"));

// Per-IP rate limit for anonymous, abuse-prone endpoints (login, client-error log).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Auth, httpCtx =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpCtx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0,
            }));
});

// Full-dashboard assistant — domain handlers + router
builder.Services.AddScoped<IDomainHandler, WicLeaveHandler>();
builder.Services.AddScoped<IDomainHandler, SickLeaveHandler>();
builder.Services.AddScoped<IDomainHandler, VacationsHandler>();
builder.Services.AddScoped<IDomainHandler, ALBalanceHandler>();
builder.Services.AddScoped<IDomainHandler, DashboardHandler>();
builder.Services.AddScoped<IDomainHandler, WicForecastHandler>();
builder.Services.AddScoped<IDomainHandler, WicCoverageDetailHandler>();
builder.Services.AddScoped<IDomainHandler, PipelineHandler>();
builder.Services.AddScoped<IDomainHandler, TrainingHandler>();
builder.Services.AddScoped<IDomainHandler, EmployeesHandler>();
builder.Services.AddScoped<IDomainHandler, WicOpeningHoursHandler>();
builder.Services.AddScoped<IDomainHandler, AgentAvailabilityHandler>();
builder.Services.AddScoped<AssistantService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "EON GSD Dashboard API", Version = "v1" });
    // WicAssistant and Assistant both declare an AskRequest record — without this,
    // Swashbuckle throws a schemaId clash and swagger.json returns 500.
    c.CustomSchemaIds(t => t.FullName);
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    ctx.Response.StatusCode  = 500;
    ctx.Response.ContentType = "application/json";
    var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var logger  = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogError(feature?.Error, "Unhandled exception on {Method} {Path}",
        ctx.Request.Method, ctx.Request.Path);
    await ctx.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
}));

// /swagger is NOT under /api, so RbacMiddleware never sees it — over the devtunnel it
// would publish the full endpoint map to anyone. Gate it explicitly: DEV role only,
// ALWAYS enforced (independent of the Auth:EnforceAuthorization rollout flag).
// Everyone else gets 404, not 401/403, so the endpoint map's existence is not leaked.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/swagger"))
    {
        var auth = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded || auth.Principal?.FindFirst(AuthClaims.Role)?.Value != AppRoles.Dev)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
    }
    await next();
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "GSD Dashboard API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseRbac();
app.UseDefaultFiles();

// Ensure BoEntries table exists (idempotent)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BoEntries')
        CREATE TABLE BoEntries (
            Id           INT           IDENTITY(1,1) PRIMARY KEY,
            EntryDate    DATE          NOT NULL,
            EmployeeName NVARCHAR(200) NOT NULL,
            ShiftStart   NVARCHAR(10)  NOT NULL DEFAULT '08:00',
            ShiftEnd     NVARCHAR(10)  NOT NULL DEFAULT '17:00',
            Note         NVARCHAR(500) NULL,
            SortOrder    INT           NOT NULL DEFAULT 0
        )
    """);
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Bo_Date') CREATE INDEX IX_Bo_Date ON BoEntries (EntryDate)");
}

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

// WicOpeningHours — add EffectiveFrom + ChangeNote columns (idempotent)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('WicOpeningHours') AND name='EffectiveFrom')
            ALTER TABLE WicOpeningHours ADD EffectiveFrom DATE NULL
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('WicOpeningHours') AND name='ChangeNote')
            ALTER TABLE WicOpeningHours ADD ChangeNote NVARCHAR(500) NULL
    """);
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_WicHours_Effective') CREATE INDEX IX_WicHours_Effective ON WicOpeningHours (LocationCode, DayOfWeek, EffectiveFrom)");
}

// RtmEntries table (idempotent)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
    db.Database.ExecuteSqlRaw("""
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
        )
    """);
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Rtm_Date') CREATE INDEX IX_Rtm_Date ON RtmEntries (EntryDate)");
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Rtm_Emp')  CREATE INDEX IX_Rtm_Emp  ON RtmEntries (EmployeeId)");
}

// RosterBatches table (idempotent) — roster generation audit; ShiftEntries rows
// point back via SourceModule='Roster' + SourceId=RosterBatches.Id.
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'RosterBatches')
        CREATE TABLE RosterBatches (
            Id              INT           IDENTITY(1,1) PRIMARY KEY,
            EmployeeId      NVARCHAR(20)  NOT NULL,
            FullName        NVARCHAR(200) NULL,
            DateFrom        DATE          NOT NULL,
            DateTo          DATE          NOT NULL,
            ShiftStart      NVARCHAR(10)  NOT NULL DEFAULT '08:00',
            ShiftEnd        NVARCHAR(10)  NOT NULL DEFAULT '17:00',
            WorkingDays     NVARCHAR(30)  NOT NULL DEFAULT '1,2,3,4,5',
            AgentTask       NVARCHAR(20)  NULL,
            LocationCode    NVARCHAR(50)  NULL,
            [RowCount]        INT           NOT NULL DEFAULT 0,
            SkippedExisting INT           NOT NULL DEFAULT 0,
            SkippedHolidays INT           NOT NULL DEFAULT 0,
            CreatedByKid    NVARCHAR(20)  NULL,
            CreatedByName   NVARCHAR(200) NULL,
            CreatedAt       DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
            DeletedAt       DATETIME2     NULL
        )
    """);
    // Existing databases created before WIC rosters: add the column idempotently.
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('RosterBatches') AND name='LocationCode') ALTER TABLE RosterBatches ADD LocationCode NVARCHAR(50) NULL");
    // Overwrite mode (roster generator): replaced-row count + JSON snapshot used
    // by DELETE /api/roster/batches/{id} to restore the pre-batch state.
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('RosterBatches') AND name='ReplacedExisting') ALTER TABLE RosterBatches ADD ReplacedExisting INT NOT NULL DEFAULT 0");
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('RosterBatches') AND name='ReplacedRowsJson') ALTER TABLE RosterBatches ADD ReplacedRowsJson NVARCHAR(MAX) NULL");
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RosterBatch_Emp')     CREATE INDEX IX_RosterBatch_Emp     ON RosterBatches (EmployeeId)");
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RosterBatch_Created') CREATE INDEX IX_RosterBatch_Created ON RosterBatches (CreatedAt)");
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
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Employees') AND name='ShiftPattern') ALTER TABLE Employees ADD ShiftPattern nvarchar(20)  NULL
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

// ── Auth: AppUserRoles table + idempotent seed ────────────────────────────────
// LOCKOUT PROTECTION (hard requirement): the app refuses to start unless at least
// one ACTIVE DEV row with a non-empty Kid exists in AppUserRoles.
{
    using var scope = app.Services.CreateScope();
    var db     = scope.ServiceProvider.GetRequiredService<GSDContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AppUserRoles')
        CREATE TABLE AppUserRoles (
            Id          INT            IDENTITY(1,1) PRIMARY KEY,
            Kid         NVARCHAR(20)   NOT NULL DEFAULT '',
            Role        NVARCHAR(20)   NOT NULL,
            EmployeeId  NVARCHAR(20)   NULL,
            DisplayName NVARCHAR(200)  NOT NULL,
            IsActive    BIT            NOT NULL DEFAULT 1,
            CreatedAt   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
            UpdatedAt   DATETIME2      NULL
        )
    """);
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_AppUserRoles_Kid' AND object_id = OBJECT_ID('AppUserRoles'))
            CREATE UNIQUE INDEX UX_AppUserRoles_Kid ON AppUserRoles (Kid) WHERE Kid <> ''
    """);

    // Seed: DEV + RTMs (idempotent). These KIDs exist in NEITHER Employees.PrimaryKid
    // NOR Employees.SecondaryKid — AppUserRoles is their only login path (rule a).
    if (builder.Environment.IsDevelopment())
    {
        // Development seed: DEV + RTM + TEAM_LEAD placeholders. Values come from
        // non-committed configuration (AppUserRoles:*) in appsettings.Local.json,
        // so no login identifier or employee name is hard-coded in source.
        // Production does NOT seed these rows; they are managed in the database.
        var seedUsers = builder.Configuration.GetSection("AppUserRoles:SeedUsers").GetChildren()
            .Select(x => new
            {
                Kid         = x["Kid"] ?? "",
                Role        = x["Role"] ?? "",
                EmployeeId  = x["EmployeeId"],
                DisplayName = x["DisplayName"] ?? "",
            })
            .ToList();
        foreach (var u in seedUsers)
            db.Database.ExecuteSqlRaw(
                """
                IF NOT EXISTS (SELECT 1 FROM AppUserRoles WHERE Kid = {0})
                    INSERT INTO AppUserRoles (Kid, Role, EmployeeId, DisplayName, IsActive)
                    VALUES ({0}, {1}, {2}, {3}, 1);
                """, u.Kid, u.Role, u.EmployeeId!, u.DisplayName);

        // TEAM_LEAD placeholders: Kid = '' AND IsActive = 0 -> can NEVER authenticate.
        // Fill Kid + IsActive = 1 once the real KIDs are provided (see documentation/auth_rbac.md).
        var teamLeads = builder.Configuration.GetSection("AppUserRoles:TeamLeads").Get<string[]>()
            ?? Array.Empty<string>();
        foreach (var name in teamLeads)
            db.Database.ExecuteSqlRaw(
                """
                IF NOT EXISTS (SELECT 1 FROM AppUserRoles WHERE Role = 'TEAM_LEAD' AND DisplayName = {0})
                    INSERT INTO AppUserRoles (Kid, Role, EmployeeId, DisplayName, IsActive)
                    VALUES ('', 'TEAM_LEAD', NULL, {0}, 0);
                """, name);
    }

    // Hard invariant: an AGENT session must always carry a real employeeId.
    var badAgents = db.Database
        .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM AppUserRoles WHERE Role = 'AGENT' AND (EmployeeId IS NULL OR EmployeeId = '')")
        .Single();
    if (badAgents > 0)
        throw new InvalidOperationException(
            $"AppUserRoles contains {badAgents} AGENT row(s) without EmployeeId. " +
            "An AGENT session must always carry a real employeeId. Fix or delete those rows; the app will not start.");

    // Lockout protection: never start with no active DEV login.
    var activeDevs = db.Database
        .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM AppUserRoles WHERE Role = 'DEV' AND IsActive = 1 AND Kid <> ''")
        .Single();
    if (activeDevs == 0)
        throw new InvalidOperationException(
            "No active DEV row in AppUserRoles. Run the recovery INSERT from documentation/auth_rbac.md; the app will not start.");
    logger.LogInformation("Auth seed OK: {DevCount} active DEV login(s) present", activeDevs);
}

// Schema migrations (idempotent — safe on every startup)
{
    using var scope = app.Services.CreateScope();
    var db     = scope.ServiceProvider.GetRequiredService<GSDContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Vacations.WorkDaysNet: INT → DECIMAL(10,1)
    db.Database.ExecuteSqlRaw("""
        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('Vacations') AND name = 'WorkDaysNet'
              AND system_type_id = TYPE_ID('int')
        )
            ALTER TABLE Vacations ALTER COLUMN WorkDaysNet DECIMAL(10,1) NULL
    """);

    // ALBalance.PlannedTakenAL: INT → DECIMAL(10,1)
    // DEFAULT constraint must be dropped first — auto-named, so look it up dynamically.
    db.Database.ExecuteSqlRaw("""
        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('ALBalance') AND name = 'PlannedTakenAL'
              AND system_type_id = TYPE_ID('int')
        )
        BEGIN
            DECLARE @cn1 NVARCHAR(200)
            SELECT @cn1 = dc.name FROM sys.default_constraints dc
            JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID('ALBalance') AND c.name = 'PlannedTakenAL'
            IF @cn1 IS NOT NULL EXEC('ALTER TABLE ALBalance DROP CONSTRAINT ' + @cn1)
            ALTER TABLE ALBalance ALTER COLUMN PlannedTakenAL DECIMAL(10,1) NOT NULL
            ALTER TABLE ALBalance ADD DEFAULT 0 FOR PlannedTakenAL
        END
    """);

    // ALBalance.RemainingAL: INT → DECIMAL(10,1)
    db.Database.ExecuteSqlRaw("""
        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('ALBalance') AND name = 'RemainingAL'
              AND system_type_id = TYPE_ID('int')
        )
        BEGIN
            DECLARE @cn2 NVARCHAR(200)
            SELECT @cn2 = dc.name FROM sys.default_constraints dc
            JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID('ALBalance') AND c.name = 'RemainingAL'
            IF @cn2 IS NOT NULL EXEC('ALTER TABLE ALBalance DROP CONSTRAINT ' + @cn2)
            ALTER TABLE ALBalance ALTER COLUMN RemainingAL DECIMAL(10,1) NOT NULL
            ALTER TABLE ALBalance ADD DEFAULT 0 FOR RemainingAL
        END
    """);

    // ALBalance.EligibleDays: INT → DECIMAL(10,2) (allows half/2-decimal eligible-day values, e.g. 26.5)
    db.Database.ExecuteSqlRaw("""
        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('ALBalance') AND name = 'EligibleDays'
              AND system_type_id = TYPE_ID('int')
        )
        BEGIN
            DECLARE @cn3 NVARCHAR(200)
            SELECT @cn3 = dc.name FROM sys.default_constraints dc
            JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID('ALBalance') AND c.name = 'EligibleDays'
            IF @cn3 IS NOT NULL EXEC('ALTER TABLE ALBalance DROP CONSTRAINT ' + @cn3)
            ALTER TABLE ALBalance ALTER COLUMN EligibleDays DECIMAL(10,2) NOT NULL
            ALTER TABLE ALBalance ADD DEFAULT 28 FOR EligibleDays
        END
    """);

    // ALBalance.Year: add column so EligibleDays can be tracked per employee per calendar
    // year. Existing rows default to the current year, so nothing changes for anyone
    // until a per-year value is explicitly edited.
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ALBalance') AND name = 'Year')
            ALTER TABLE ALBalance ADD Year INT NOT NULL DEFAULT (YEAR(GETDATE()))
    """);

    // ALBalance unique constraint: (EmployeeId) → (EmployeeId, Year), now that a single
    // employee can have one ALBalance row per year.
    db.Database.ExecuteSqlRaw("""
        IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_ALBalance_EmpId' AND parent_object_id = OBJECT_ID('ALBalance'))
            ALTER TABLE ALBalance DROP CONSTRAINT UQ_ALBalance_EmpId
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('ALBalance') AND name = 'UQ_ALBalance_EmpYear')
            CREATE UNIQUE INDEX UQ_ALBalance_EmpYear ON ALBalance (EmployeeId, Year)
    """);

    // Manual-change marker + manual exception days (added 2026-10-01). Additive and
    // nullable only: no backfill, no existing row is rewritten, every statement is
    // idempotent -- same ExecuteSqlRaw pattern as the ALBalance index above.
    //   ShiftEntries.ManualChangeBy/At/Note  - a human deliberately set this row
    //   WicOpeningHours.ExceptionDate        - this row applies to that ONE date only
    //   WicOpeningHours.ManualChangeBy/At    - who recorded the exception
    db.Database.ExecuteSqlRaw("""
        IF COL_LENGTH('ShiftEntries','ManualChangeBy') IS NULL
            ALTER TABLE ShiftEntries ADD ManualChangeBy NVARCHAR(100) NULL
        IF COL_LENGTH('ShiftEntries','ManualChangeAt') IS NULL
            ALTER TABLE ShiftEntries ADD ManualChangeAt DATETIME2 NULL
        IF COL_LENGTH('ShiftEntries','ManualChangeNote') IS NULL
            ALTER TABLE ShiftEntries ADD ManualChangeNote NVARCHAR(500) NULL
        IF COL_LENGTH('WicOpeningHours','ExceptionDate') IS NULL
            ALTER TABLE WicOpeningHours ADD ExceptionDate DATE NULL
        IF COL_LENGTH('WicOpeningHours','ManualChangeBy') IS NULL
            ALTER TABLE WicOpeningHours ADD ManualChangeBy NVARCHAR(100) NULL
        IF COL_LENGTH('WicOpeningHours','ManualChangeAt') IS NULL
            ALTER TABLE WicOpeningHours ADD ManualChangeAt DATETIME2 NULL
    """);

    // ROLLOUT (added 2026-10-08): the manual "Rollout" page. Two NEW tables only --
    // additive and idempotent, no existing table or column is altered and no existing row
    // is written by this migration. WicRolloutDays is the authority for one WIC centre
    // opened on one date for new workers (hours + who entered it); WicRolloutWorkers holds
    // the selected workers and the ids of the assignment rows the rollout created, so an
    // edit or a delete touches only what came from it. See documentation/ROLLOUT.md.
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WicRolloutDays')
        CREATE TABLE WicRolloutDays (
            Id               INT            IDENTITY(1,1) PRIMARY KEY,
            LocationCode     NVARCHAR(50)   NOT NULL,
            LocationName     NVARCHAR(200)  NULL,
            RolloutDate      DATE           NOT NULL,
            StartTime        NVARCHAR(5)    NOT NULL,
            EndTime          NVARCHAR(5)    NOT NULL,
            Note             NVARCHAR(1000) NULL,
            IsManual         BIT            NOT NULL DEFAULT 1,
            ManualChangeBy   NVARCHAR(100)  NULL,
            ManualChangeAt   DATETIME2      NULL,
            ManualChangeNote NVARCHAR(500)  NULL,
            HoursRowId       INT            NULL,
            CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
        )
    """);
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WicRolloutWorkers')
        CREATE TABLE WicRolloutWorkers (
            Id              INT           IDENTITY(1,1) PRIMARY KEY,
            RolloutDayId    INT           NOT NULL REFERENCES WicRolloutDays(Id) ON DELETE CASCADE,
            EmployeeId      NVARCHAR(20)  NOT NULL,
            EmployeeName    NVARCHAR(200) NULL,
            FromPipeline    BIT           NOT NULL DEFAULT 0,
            PipelineNote    NVARCHAR(500) NULL,
            ShiftEntryId    INT           NULL,
            WicShiftEntryId INT           NULL
        )
    """);
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UQ_WicRolloutDays_LocDate' AND object_id=OBJECT_ID('WicRolloutDays')) CREATE UNIQUE INDEX UQ_WicRolloutDays_LocDate ON WicRolloutDays (LocationCode, RolloutDate)");
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_WicRollout_Date' AND object_id=OBJECT_ID('WicRolloutDays')) CREATE INDEX IX_WicRollout_Date ON WicRolloutDays (RolloutDate)");
    db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UQ_WicRolloutWorkers_DayEmp' AND object_id=OBJECT_ID('WicRolloutWorkers')) CREATE UNIQUE INDEX UQ_WicRolloutWorkers_DayEmp ON WicRolloutWorkers (RolloutDayId, EmployeeId)");

    // ON DEMAND / NOT ROSTERED BY US (owner decision, 2026-10-16). Additive and defaulted
    // false: an employee engaged only when needed works on another project and has no
    // roster of his own. The flag silences ONLY the Overview "Roster gaps" banner
    // (GET /api/roster/missing); the employee stays active and assignable by hand and is
    // still offered by the substitute/candidate suggestions. No existing row is changed.
    db.Database.ExecuteSqlRaw("""
        IF COL_LENGTH('Employees','IsOnDemand') IS NULL
            ALTER TABLE Employees ADD IsOnDemand BIT NOT NULL CONSTRAINT DF_Employees_IsOnDemand DEFAULT 0
    """);

    // Clear IsWicDuty on any non-WIC_DUTY row (safe: sets corrupt 1→0, never removes rows)
    var wicDutyFixed = db.Database.ExecuteSqlRaw("""
        UPDATE ShiftEntries
        SET IsWicDuty = 0
        WHERE IsWicDuty = 1
          AND ShiftType != 'WIC_DUTY'
          AND ManualChangeBy IS NULL   -- a manual decision is never "corrupt"; never touch it
    """);
    if (wicDutyFixed > 0)
        logger.LogWarning("Startup cleanup: cleared IsWicDuty=1 on {Count} non-WIC_DUTY ShiftEntries", wicDutyFixed);

    // Report-only integrity check (never mutates): ShiftEntries rows whose AgentTask
    // names a WIC centre while ShiftType is WORKING. That pair is the WIC_DUTY encoding
    // with a flipped ShiftType — AvailabilityResolver.IsWicDutyAt now reads it correctly
    // for coverage, so it no longer produces phantom "UNSTAFFED" days, but the row is
    // still wrong on the Shifts grid and only an RTM may correct ShiftType (roster
    // truth). Known source: RosterBatch 155/156/170, CreatedByKid=SYSTEM_WICFIX,
    // 2026-09-30 06:09, overwrite mode.
    try
    {
        var badges = AgentTasks.NonLocationValues;
        var suspects = db.ShiftEntries
            .Where(e => e.ShiftType == ShiftTypes.Working
                     && e.AgentTask != null && e.AgentTask != ""
                     && e.ManualChangeBy == null   // a manual change is not a data defect
                     && !badges.Contains(e.AgentTask))
            .Select(e => new { e.Id, e.EmployeeId, e.ShiftDate, e.AgentTask })
            .ToList();
        if (suspects.Count > 0)
        {
            var activeLocations = db.WicLocations.Where(l => l.IsActive).ToList();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var flipped = suspects
                .Where(x => activeLocations.Any(l => WicLocationMatcher.MatchesAgentTask(x.AgentTask, l)))
                .ToList();
            if (flipped.Count > 0)
                logger.LogWarning(
                    "Data integrity: {Total} ShiftEntries rows ({Future} dated today or later) have ShiftType=WORKING " +
                    "but an AgentTask naming an active WIC centre — WIC duty with a flipped ShiftType. " +
                    "Counted as coverage by AvailabilityResolver.IsWicDutyAt; ShiftType left untouched. Rows: {Ids}",
                    flipped.Count, flipped.Count(x => x.ShiftDate >= today),
                    string.Join(", ", flipped.OrderBy(x => x.ShiftDate).Take(20)
                        .Select(x => $"#{x.Id} {x.EmployeeId} {x.ShiftDate:yyyy-MM-dd} '{x.AgentTask}'")));
        }
    }
    catch (Exception ex)
    {
        // A diagnostic must never be able to stop the app from starting.
        logger.LogWarning(ex, "Data integrity: flipped-ShiftType WIC check failed to run");
    }

    // NOTE: orphan WicShiftEntries cleanup (NULL SupportLocation) is intentionally NOT here.
    // Run POST /api/admin/cleanup-wic-orphans manually after reviewing the count.
}

// index.html is the SPA shell that points at the current content-hashed bundle —
// it must never be cached, or browsers keep loading a stale bundle reference after
// a redeploy. The /assets/*.js|css files ARE content-hashed (filename changes when
// content changes), so those are safe to cache forever.
var staticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
        else if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        }
    }
};
app.UseStaticFiles(staticFileOptions);

app.MapAuthEndpoints();
app.MapKioskEndpoints();
app.MapDashboardEndpoints();
app.MapShiftEndpoints();
app.MapWicEndpoints();
app.MapWicCardsEndpoints();
app.MapWicCoverageGapEndpoints();
app.MapWicAssignCandidatesEndpoints();
app.MapVwicDailyEndpoints();
app.MapAttendanceEndpoints();
app.MapSickLeaveEndpoints();
app.MapVacationEndpoints();
app.MapPublicHolidayEndpoints();
app.MapPipelineEndpoints();
app.MapRolloutEndpoints();
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
app.MapBoListEndpoints();
app.MapBulkRtmEndpoints();
app.MapRosterEndpoints();
app.MapWicAssistantEndpoints();
app.MapAssistantEndpoints();
app.MapWicMigrationEndpoints();
app.MapDemoDataAdminEndpoints();
app.MapHrExportEndpoints();
app.MapFeaturesEndpoints();
app.MapMyEndpoints();

// Manual one-shot cleanup: removes WicShiftEntries rows where SupportLocation IS NULL
// and a non-NULL sibling exists for the same (EmployeeId, ShiftDate).
// These are ghost rows from partial writes caused by the B1 transaction bug (pre-fix).
// Run once after deployment, verify the count, then it becomes a no-op.
app.MapPost("/api/admin/cleanup-wic-orphans", (GSDContext db, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("AdminCleanup");
    var deleted = db.Database.ExecuteSqlRaw("""
        DELETE w
        FROM WicShiftEntries w
        WHERE w.SupportLocation IS NULL
          AND EXISTS (
              SELECT 1 FROM WicShiftEntries w2
              WHERE w2.EmployeeId = w.EmployeeId
                AND w2.ShiftDate  = w.ShiftDate
                AND w2.SupportLocation IS NOT NULL
          )
    """);
    logger.LogInformation("cleanup-wic-orphans: deleted {Count} rows", deleted);
    return Results.Ok(new { deleted, message = $"Deleted {deleted} orphan WicShiftEntry rows" });
}).WithTags("Admin");

app.MapGet("/api/wic/conflicts", async (string? from, string? to, bool? includeSplitShifts, WicConflictDetector svc) =>
{
    var fromDate = from != null && DateOnly.TryParse(from, out var f) ? f : DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    var toDate   = to   != null && DateOnly.TryParse(to,   out var t) ? t : DateOnly.FromDateTime(DateTime.Today);
    return Results.Ok(await svc.GetConflictsAsync(fromDate, toDate, includeSplitShifts ?? false));
}).WithTags("WIC");

app.MapPost("/api/debug/client-error", async (HttpContext ctx) =>
{
    const int maxBodyBytes = 8_192;
    const long maxLogBytes = 5 * 1024 * 1024;
    var buf = new byte[maxBodyBytes];
    var read = await ctx.Request.Body.ReadAsync(buf.AsMemory(0, maxBodyBytes));
    var body = System.Text.Encoding.UTF8.GetString(buf, 0, read);
    var logPath = Path.Combine(AppContext.BaseDirectory, "client-errors.log");
    var fi = new FileInfo(logPath);
    if (fi.Exists && fi.Length >= maxLogBytes) return Results.Ok();
    await File.AppendAllTextAsync(logPath,
        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{body}\n---\n");
    return Results.Ok();
}).WithTags("Debug").RequireRateLimiting(RateLimitPolicies.Auth);

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));
app.MapFallbackToFile("index.html", staticFileOptions);
// Vacations imported straight into the table never triggered the shift sync -
// reconcile future leave days with the roster at startup.
using (var vacScope = app.Services.CreateScope())
{
    try
    {
        var vacSync = vacScope.ServiceProvider.GetRequiredService<GSDDashboard.API.Services.ShiftSyncService>();
        var vacFixed = await vacSync.ReconcileFutureVacationsAsync();
        Console.WriteLine("Vacation reconcile: " + vacFixed + " future leave day(s) corrected");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Vacation reconcile failed: " + ex.Message);
    }
}

app.Run();
