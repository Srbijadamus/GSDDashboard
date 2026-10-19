using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Vacations;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GSDDashboard.Tests;

// ── THE UNIFIED "ON AL" RULE ───────────────────────────────────────────────────
//
// Vacations "On AL today" and Overview "Absent today" (AL) must read the SAME set:
//   AL in the Shift Plan OR the AL Tracker = AL.
// One cause per test, mirroring the live 2026-10-19 diagnosis (12 AL in the Shift
// Plan vs 8 in the AL Tracker, all four extra people being Shift-Plan-only AL):
//   1. AL only in the Shift Plan          -> counted
//   2. AL only in the AL Tracker          -> counted
//   3. manual RTM duty overrides tracker  -> not counted
//   4. rejected/cancelled tracker         -> not counted
//   5. inactive employee                  -> not counted
//   6. resigned employee                  -> not counted
//   7. HALF_AL                            -> counted as a half day
public class AlUnionRuleTests
{
    private static readonly DateOnly Day = new(2026, 10, 19);

    private static GSDContext NewDb() => new(
        new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Employee Active(string id) =>
        new() { EmployeeId = id, FullName = $"Agent {id}", IsActive = true };

    private static ShiftEntry Al(string id) =>
        new() { EmployeeId = id, ShiftDate = Day, ShiftType = ShiftTypes.AnnualLeave };

    private static Vacation Tracker(string id, string? status = null, decimal? workDaysNet = null) => new()
    {
        EmployeeId = id, FirstDay = Day, LastDay = Day, WorkDaysNet = workDaysNet, ApprovedDenied = status,
    };

    [Fact]
    public async Task ShiftPlanOnlyAl_IsCounted()
    {
        using var db = NewDb();
        db.Employees.Add(Active("E1"));
        db.ShiftEntries.Add(Al("E1"));
        await db.SaveChangesAsync();

        var r = await AvailabilityResolver.GetAlOnDateAsync(db, Day);

        Assert.Equal(1, r.FullCount);
        Assert.Equal(0, r.HalfCount);
        Assert.Contains(r.People, p => p.EmployeeId == "E1" && !p.IsHalfDay);
    }

    [Fact]
    public async Task TrackerOnlyAl_IsCounted()
    {
        using var db = NewDb();
        db.Employees.Add(Active("E2"));
        db.Vacations.Add(Tracker("E2"));
        await db.SaveChangesAsync();

        var r = await AvailabilityResolver.GetAlOnDateAsync(db, Day);

        Assert.Equal(1, r.FullCount);
        Assert.Contains(r.People, p => p.EmployeeId == "E2" && !p.IsHalfDay);
    }

    [Fact]
    public async Task ManualDuty_OverridesTracker_NotCounted()
    {
        using var db = NewDb();
        db.Employees.Add(Active("E3"));
        db.Vacations.Add(Tracker("E3"));
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "E3", ShiftDate = Day, ShiftType = ShiftTypes.WicDuty,
            IsWicDuty = true, ManualChangeBy = "T-PLAN",
        });
        await db.SaveChangesAsync();

        var r = await AvailabilityResolver.GetAlOnDateAsync(db, Day);

        Assert.Empty(r.People);
    }

    [Fact]
    public async Task RejectedTracker_NotCounted()
    {
        using var db = NewDb();
        db.Employees.Add(Active("E4"));
        db.Vacations.Add(Tracker("E4", "Denied"));
        await db.SaveChangesAsync();

        var r = await AvailabilityResolver.GetAlOnDateAsync(db, Day);

        Assert.Empty(r.People);
    }

    [Fact]
    public async Task InactiveEmployee_NotCounted()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = "E5", FullName = "Inactive", IsActive = false });
        db.ShiftEntries.Add(Al("E5"));
        await db.SaveChangesAsync();

        var r = await AvailabilityResolver.GetAlOnDateAsync(db, Day);

        Assert.Empty(r.People);
    }

    [Fact]
    public async Task ResignedEmployee_NotCounted()
    {
        using var db = NewDb();
        db.Employees.Add(Active("E6"));
        db.Vacations.Add(Tracker("E6"));
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "E6", ShiftDate = Day, ShiftType = ShiftTypes.Resigned });
        await db.SaveChangesAsync();

        var r = await AvailabilityResolver.GetAlOnDateAsync(db, Day);

        Assert.Empty(r.People);
    }

    [Fact]
    public async Task HalfAl_IsCountedAsHalf()
    {
        using var db = NewDb();
        db.Employees.Add(Active("E7"));
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "E7", ShiftDate = Day, ShiftType = ShiftTypes.HalfAL });
        await db.SaveChangesAsync();

        var r = await AvailabilityResolver.GetAlOnDateAsync(db, Day);

        Assert.Equal(0, r.FullCount);
        Assert.Equal(1, r.HalfCount);
        Assert.Contains(r.People, p => p.EmployeeId == "E7" && p.IsHalfDay);
    }

    [Fact]
    public async Task ManualAl_IsCounted()
    {
        // A manual edit TO an absence is a decision about the day — still AL.
        using var db = NewDb();
        db.Employees.Add(Active("E8"));
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "E8", ShiftDate = Day, ShiftType = ShiftTypes.AnnualLeave, ManualChangeBy = "T-PLAN",
        });
        await db.SaveChangesAsync();

        var r = await AvailabilityResolver.GetAlOnDateAsync(db, Day);

        Assert.Equal(1, r.FullCount);
    }

    // ── VacationService endpoints read the same rule ──────────────────────────

    [Fact]
    public async Task VacationService_Active_UsesTheUnion()
    {
        using var db = NewDb();
        db.Employees.Add(Active("V1"));
        db.Employees.Add(Active("V2"));
        db.ShiftEntries.Add(Al("V1"));          // Shift-Plan-only
        db.Vacations.Add(Tracker("V2"));        // Tracker-only
        await db.SaveChangesAsync();

        var svc = new VacationService(db, new ShiftSyncService(db));
        var active = await svc.GetActiveOnDateAsync(Day);

        Assert.Equal(2, active.Count);
        Assert.Contains(active, a => a.EmployeeId == "V1" && !a.IsHalfDay);
        Assert.Contains(active, a => a.EmployeeId == "V2" && !a.IsHalfDay);
    }

    [Fact]
    public async Task VacationService_Upcoming_UsesTheUnion()
    {
        using var db = NewDb();
        db.Employees.Add(Active("U1"));
        db.Employees.Add(Active("U2"));
        var start = Day.AddDays(1);
        db.Vacations.Add(new Vacation { EmployeeId = "U1", FirstDay = start, LastDay = start.AddDays(2) });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "U2", ShiftDate = start, ShiftType = ShiftTypes.AnnualLeave });
        await db.SaveChangesAsync();

        var svc = new VacationService(db, new ShiftSyncService(db));
        var upcoming = await svc.GetUpcomingAsync(7);

        Assert.Equal(2, upcoming.Count);
        Assert.Contains(upcoming, u => u.EmployeeId == "U1");
        Assert.Contains(upcoming, u => u.EmployeeId == "U2");
    }

    // ── Briefing "Absent today" reads the same rule ───────────────────────────

    [Fact]
    public async Task Briefing_AbsentToday_IncludesTrackerOnlyAl()
    {
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
            db.Employees.Add(Active("B1"));        // tracker-only AL
            db.Employees.Add(Active("B2"));        // Shift-Plan AL
            db.Vacations.Add(Tracker("B1"));
            db.ShiftEntries.Add(Al("B2"));
            await db.SaveChangesAsync();
        }

        using var scope = sp.CreateScope();
        var briefing = await scope.ServiceProvider.GetRequiredService<BriefingService>()
            .GetBriefingAsync(Day.ToString("yyyy-MM-dd"));

        Assert.Contains(briefing.Absences, a => a.EmployeeId == "B1");
        Assert.Contains(briefing.Absences, a => a.EmployeeId == "B2");
        Assert.Equal(2, briefing.Absences.Count(a =>
            a.ShiftType.Equals(ShiftTypes.AnnualLeave, StringComparison.OrdinalIgnoreCase)));
    }
}
