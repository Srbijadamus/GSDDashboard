using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Employees;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GSDDashboard.Tests;

// ── AL TRACKER vs SHIFT PLAN (owner decision 2026-10-16: the Shift Plan wins) ──────────
//
// The reported defect: RTM removes an AL in the Shift Plan by hand and puts a duty on the
// day, but the AL Tracker (Vacations, the "Annual Leave" tab) still holds the approved
// leave. Every reader then saw the agent as ABSENT (AL) in the WIC cards, the kiosk
// check-in status, /api/wic/open, the Briefing and the forecast.
//
// The rule pinned here:
//   * a ShiftEntries row with ManualChangeBy set to a NON-absence type (WORKING, WIC_DUTY,
//     BO, ...) means the tracker row for that employee+date is IGNORED;
//   * the agent is therefore present, and the leftover tracker row is reported as a
//     CONFLICT (date, EmployeeId, AL entry id) for whoever maintains the AL tracker;
//   * a tracker row with NO manual edit still means absent (unchanged);
//   * a manual edit TO an absence is honoured, and HALF_AL keeps its existing half-day
//     semantics (never in the override set);
//   * the AL Tracker is never changed and nothing is deleted.
public class AlTrackerOverrideTests
{
    private const string Code = "DE~99999~Testheim~Teststr. 1";
    private const string Name = "Testheim";
    private const string Emp = "T1";

    // A Tuesday, so the weekend skip in the leave sync never matters.
    private static readonly DateOnly Day = new(2026, 10, 20);

    private static DbContextOptions<GSDContext> Options() =>
        new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static GSDContext NewDb() => new(Options());

    private static void SeedCentre(GSDContext db)
    {
        db.WicLocations.Add(new WicLocation
        {
            Id = 1, LocationCode = Code, DisplayName = Name, City = "Testheim",
            MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = (int)Day.DayOfWeek,
            OpenTime = "08:00", CloseTime = "17:00",
        });
        db.WicAgentAssignments.Add(new WicAgentAssignment
        {
            LocationCode = Code, EmployeeName = "Test Main", AssignmentType = "MAIN", IsActive = true,
        });
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = "Test Main", IsActive = true });
    }

    private static ShiftEntry ManualDuty() => new()
    {
        EmployeeId = Emp, ShiftDate = Day, ShiftType = ShiftTypes.WicDuty,
        AgentTask = Name, ShiftStart = "07:30", ShiftEnd = "16:30", IsWicDuty = true,
        ManualChangeBy = "T-PLAN", ManualChangeAt = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc),
    };

    private static void AddTracker(GSDContext db, int id = 1377) => db.Vacations.Add(new Vacation
    {
        Id = id, EmployeeId = Emp, FirstDay = Day.AddDays(-1), LastDay = Day,
        WorkDaysNet = 2, ApprovedDenied = "Approved",
    });

    // ── The shared predicate ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("WORKING", true)]
    [InlineData("WIC_DUTY", true)]
    [InlineData("BO", true)]
    [InlineData("TRAINING", true)]
    [InlineData("CD", true)]
    [InlineData("AL", false)]
    [InlineData("SL", false)]
    [InlineData("UL", false)]
    [InlineData("OL", false)]
    [InlineData("PH", false)]
    [InlineData("LPH", false)]
    [InlineData("RESIGNED", false)]
    [InlineData("HALF_AL", false)]
    public void ManualEdit_OverridesTracker_OnlyForNonAbsenceTypes(string shiftType, bool expected)
    {
        var row = new ShiftEntry
        {
            EmployeeId = Emp, ShiftDate = Day, ShiftType = shiftType, ManualChangeBy = "T-PLAN",
        };
        Assert.Equal(expected, AlTrackerOverrides.IsManualNonAbsenceDuty(row));
    }

    [Fact]
    public void NoManualMarker_NeverOverridesTracker()
    {
        var row = new ShiftEntry { EmployeeId = Emp, ShiftDate = Day, ShiftType = ShiftTypes.Working };
        Assert.False(AlTrackerOverrides.IsManualNonAbsenceDuty(row));
        Assert.False(AlTrackerOverrides.IsManualNonAbsenceDuty(null));
    }

    [Fact]
    public void Resolve_ManualDuty_IgnoresEveryCoveringTrackerRow_AndReportsEachConflict()
    {
        var rows = new List<Vacation>
        {
            new() { Id = 11, EmployeeId = Emp, FirstDay = Day, LastDay = Day },
            new() { Id = 12, EmployeeId = Emp, FirstDay = Day.AddDays(-1), LastDay = Day.AddDays(1) },
            new() { Id = 13, EmployeeId = "OTHER", FirstDay = Day, LastDay = Day },
        };
        var byEmp = new Dictionary<string, ShiftEntry?> { [Emp] = ManualDuty() };

        var (ignore, conflicts) = AlTrackerOverrides.Resolve(Day, rows, byEmp);

        Assert.Equal(2, ignore.Count);
        Assert.Contains(11, ignore);
        Assert.Contains(12, ignore);
        Assert.DoesNotContain(13, ignore);           // another employee's entry is untouched
        Assert.Equal(2, conflicts.Count);
        Assert.All(conflicts, c => Assert.Equal(Emp, c.EmployeeId));
        Assert.All(conflicts, c => Assert.Equal("2026-10-20", c.Date));
        Assert.Contains(conflicts, c => c.AlTrackerEntryId == 11);
        Assert.Contains(conflicts, c => c.AlTrackerEntryId == 12);
    }

    // ── WicCardsService: the reader that drives kiosk check-in status ────────────

    [Fact]
    public async Task Cards_ManualDutyPlusTrackerEntry_IsPresent_AndTrackerConflictReported()
    {
        using var db = NewDb();
        SeedCentre(db);
        db.ShiftEntries.Add(ManualDuty());
        AddTracker(db);
        await db.SaveChangesAsync();

        var card = Assert.Single(await new WicCardsService(db).GetCardsAsync(Day, null));

        // NOT "UNCOVERED": the manual duty is read as cover. The exact grade is the ordinary
        // minutes-vs-window grading (the agent's 07:30-16:30 is cut to the centre's
        // 08:00-17:00, so the card can read PARTIAL) — what matters here is that the agent
        // counts and is not an absence.
        Assert.NotEqual("UNCOVERED", card.CoverageStatus);
        var agent = Assert.Single(card.AssignedAgents);
        Assert.Equal(Emp, agent.EmployeeId);
        // The absence marker must NOT be there — kiosk check-in status treats a marker as
        // "not expected", so the agent would have read as ABSENT (AL).
        Assert.Equal("07:30", agent.ShiftStart);
        Assert.Equal("16:30", agent.ShiftEnd);
        Assert.True(agent.CoveredMinutes > 0);

        var conflicts = await WicCardsService.GetAlTrackerConflictsAsync(db, Day);
        var conflict = Assert.Single(conflicts);
        Assert.Equal(Emp, conflict.EmployeeId);
        Assert.Equal(1377, conflict.AlTrackerEntryId);
        Assert.Equal("2026-10-20", conflict.Date);
        Assert.Equal("2026-10-19", conflict.TrackerFirstDay);
        Assert.Equal("2026-10-20", conflict.TrackerLastDay);
        Assert.Equal("WIC_DUTY", conflict.ShiftType);
    }

    [Fact]
    public async Task Cards_TrackerEntryWithoutManualEdit_IsStillAbsent_AndNoConflict()
    {
        using var db = NewDb();
        SeedCentre(db);
        // The tracker row and its AL ShiftEntries projection, written by the leave sync —
        // nobody has touched the day by hand.
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = Emp, ShiftDate = Day, ShiftType = ShiftTypes.AnnualLeave,
            SourceModule = "Vacation", SourceId = 1377, AutoGenerated = true,
        });
        AddTracker(db);
        await db.SaveChangesAsync();

        var card = Assert.Single(await new WicCardsService(db).GetCardsAsync(Day, null));

        Assert.Equal("UNCOVERED", card.CoverageStatus);
        // Unchanged behaviour: the leave day has no on-site WIC row, so the card stays
        // UNCOVERED with no agent listed — exactly as before this rule existed.
        Assert.Empty(card.AssignedAgents);
        Assert.Empty(await WicCardsService.GetAlTrackerConflictsAsync(db, Day));
    }

    [Fact]
    public async Task Cards_ManualHalfAlDay_KeepsExistingHalfDaySemantics()
    {
        // HALF_AL is deliberately NOT in the override set: a half-AL day is half leave and
        // half work, and its 0.5 contribution / working-part logic must not change.
        using var db = NewDb();
        SeedCentre(db);
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = Emp, ShiftDate = Day, ShiftType = ShiftTypes.HalfAL,
            ShiftStart = "08:00", ShiftEnd = "12:00",
            ManualChangeBy = "T-PLAN", ManualChangeAt = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc),
        });
        AddTracker(db);
        await db.SaveChangesAsync();

        var conflicts = await WicCardsService.GetAlTrackerConflictsAsync(db, Day);
        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task Cards_ManualAbsenceEdit_IsHonoured_AndNoConflict()
    {
        // A manual edit TO an absence is a decision about the day, not a contradiction.
        using var db = NewDb();
        SeedCentre(db);
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = Emp, ShiftDate = Day, ShiftType = ShiftTypes.AnnualLeave,
            ManualChangeBy = "T-PLAN", ManualChangeAt = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc),
        });
        AddTracker(db);
        await db.SaveChangesAsync();

        Assert.Empty(await WicCardsService.GetAlTrackerConflictsAsync(db, Day));
    }

    // ── The two other readers ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAbsentIds_ManualDuty_RemovesTheDayFromTheAbsentSet()
    {
        using var db = NewDb();
        SeedCentre(db);
        db.ShiftEntries.Add(ManualDuty());
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "T2", ShiftDate = Day, ShiftType = ShiftTypes.AnnualLeave, AutoGenerated = true,
        });
        await db.SaveChangesAsync();

        var resolver = new AvailabilityResolver(db);
        var absent = await resolver.GetAbsentIdsAsync(new[] { Emp, "T2" }, Day);

        Assert.DoesNotContain(Emp, absent);      // manual duty wins
        Assert.Contains("T2", absent);           // untouched tracker projection still absent
    }

    [Fact]
    public async Task GetStatus_ManualDuty_IsWorking_NotAl()
    {
        using var db = NewDb();
        SeedCentre(db);
        db.ShiftEntries.Add(ManualDuty());
        await db.SaveChangesAsync();

        var status = await new AvailabilityResolver(db).GetStatusAsync(Emp, Day);

        Assert.Equal(AbsenceStatus.WORKING, status);
    }

    [Fact]
    public async Task DuplicateRows_LaterLeaveRowPlusManualDuty_TheManualDutyWins()
    {
        // The exact reported shape: the manual RTM duty is the LOWER-Id row and a later
        // leave import inserted an AL row for the same employee+date. BestShiftEntry alone
        // would read AL (highest Id); the Shift Plan still wins.
        using var db = NewDb();
        SeedCentre(db);
        db.ShiftEntries.Add(ManualDuty());
        await db.SaveChangesAsync();
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = Emp, ShiftDate = Day, ShiftType = ShiftTypes.AnnualLeave,
            SourceModule = "AL_IMPORT", AutoGenerated = true,
        });
        await db.SaveChangesAsync();
        Assert.Equal(2, db.ShiftEntries.Count(s => s.EmployeeId == Emp && s.ShiftDate == Day));

        var resolver = new AvailabilityResolver(db);
        var absent = await resolver.GetAbsentIdsAsync(new[] { Emp }, Day);
        Assert.DoesNotContain(Emp, absent);
        Assert.Equal(AbsenceStatus.WORKING, await resolver.GetStatusAsync(Emp, Day));
    }

    [Fact]
    public async Task Briefing_AbsentToday_ExcludesTheManualDutyDay_AndKeepsTheUntouchedOne()
    {
        // Real DI graph, like WicPresenceUnificationTests: BriefingService pulls the whole
        // substitution chain (ReachabilityService -> IServiceScopeFactory).
        var dbName = Guid.NewGuid().ToString();
        await using var sp = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Critical))
            .AddDbContext<GSDContext>(o => o.UseInMemoryDatabase(dbName))
            .AddScoped<ReachabilityService>()
            .AddScoped<CoverageEvaluator>()
            .AddScoped<AvailabilityResolver>()
            .AddScoped<SubstitutionService>()
            .AddScoped<BriefingService>()
            .BuildServiceProvider();

        using (var seed = sp.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<GSDContext>();
            SeedCentre(db);
            db.ShiftEntries.Add(ManualDuty());
            db.ShiftEntries.Add(new ShiftEntry
            {
                EmployeeId = "T2", ShiftDate = Day, ShiftType = ShiftTypes.AnnualLeave, AutoGenerated = true,
            });
            db.Employees.Add(new Employee { EmployeeId = "T2", FullName = "Test Two", IsActive = true });
            await db.SaveChangesAsync();
        }

        using var scope = sp.CreateScope();
        var briefing = await scope.ServiceProvider.GetRequiredService<BriefingService>()
            .GetBriefingAsync(Day.ToString("yyyy-MM-dd"));

        Assert.DoesNotContain(briefing.Absences, a => a.EmployeeId == Emp);
        Assert.Contains(briefing.Absences, a => a.EmployeeId == "T2");
    }

    // ── Sync must never overwrite the manual RTM cell ────────────────────────────

    [Fact]
    public async Task VacationSync_NeverOverwritesTheManualRtmCell()
    {
        using var db = NewDb();
        SeedCentre(db);
        var manual = ManualDuty();
        db.ShiftEntries.Add(manual);
        await db.SaveChangesAsync();

        await new ShiftSyncService(db).SyncVacationAsync(Emp, Day, Day, 1377, "AL", "Vacation");

        var after = await db.ShiftEntries.AsNoTracking().SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == Day);
        Assert.Equal("WIC_DUTY", after.ShiftType);
        Assert.Equal("07:30", after.ShiftStart);
        Assert.Equal("16:30", after.ShiftEnd);
        Assert.Equal("T-PLAN", after.ManualChangeBy);
    }

    [Fact]
    public async Task FutureVacationReconciliation_NeverOverwritesTheManualRtmCell()
    {
        using var db = NewDb();
        SeedCentre(db);
        AddTracker(db);
        db.ShiftEntries.Add(ManualDuty());
        await db.SaveChangesAsync();

        var touched = await new ShiftSyncService(db).ReconcileFutureVacationsAsync();

        Assert.Equal(0, touched);
        var after = await db.ShiftEntries.AsNoTracking().SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == Day);
        Assert.Equal("WIC_DUTY", after.ShiftType);
        Assert.Equal("T-PLAN", after.ManualChangeBy);
        // The tracker row itself is untouched: this feature never deletes or rewrites it.
        Assert.Single(db.Vacations);
    }

    // ── Part B: ON DEMAND is excluded from Roster gaps, nothing else changes ─────

    [Fact]
    public async Task RosterMissing_OnDemandEmployee_IsExcluded_AndAnUnmarkedOneIsNot()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = "OD1", FullName = "On Demand", IsActive = true, IsOnDemand = true });
        db.Employees.Add(new Employee { EmployeeId = "RG1", FullName = "Real Gap", IsActive = true });
        await db.SaveChangesAsync();

        var service = new RosterService(db,
            new WicShiftService(db, new AvailabilityResolver(db)),
            new WicCoverageGapService(db));
        var result = await service.GetMissingRosterAsync(14);

        Assert.DoesNotContain(result.MissingRoster, m => m.EmployeeId == "OD1");
        Assert.Contains(result.MissingRoster, m => m.EmployeeId == "RG1");
        Assert.DoesNotContain(result.DateGapsByEmployee, g => g.EmployeeId == "OD1");
        Assert.Contains(result.DateGapsByEmployee, g => g.EmployeeId == "RG1");
        _ = today;
    }

    [Fact]
    public async Task RosterMissing_OnDemandEmployee_StaysAssignableByHand()
    {
        // The flag only silences the banner: the employee is still ACTIVE and still returned
        // by the employee list used by the Shift Plan / assignment pickers, so he stays
        // available for manual assignment and for the suggestions.
        using var db = NewDb();
        db.Employees.Add(new Employee
        {
            EmployeeId = "OD1", FullName = "On Demand", PrimaryRole = "WIC", IsActive = true, IsOnDemand = true,
        });
        await db.SaveChangesAsync();

        var listed = await new EmployeeService(db).GetEmployeesAsync(null, null, null, null, true);
        var emp = Assert.Single(listed);
        Assert.Equal("OD1", emp.EmployeeId);
        Assert.True(emp.IsOnDemand);
    }
}
