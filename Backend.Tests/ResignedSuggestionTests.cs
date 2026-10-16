using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Backup;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.Vwic;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GSDDashboard.Tests;

// 2026-11-02 Hannover defect: the Overview "Available: …" list and the
// "Gaps & recommendations" Best substitute offered agents who had already left the
// company. employee 9130648 carries ShiftEntries.ShiftType = RESIGNED from 2026-11-02
// but Employees.IsActive is still 1; 9124687 (RESIGNED from 2026-10-01) and 9087657
// (RESIGNED from 2026-11-02) still came back from Find Substitute on 2026-11-03..06.
//
// The rule pinned here, for every suggestion surface:
//   an agent is NOT offered when the Employees row is inactive, when a RESIGNED
//   ShiftEntries row is dated on or before the day being planned, or when the day's own
//   status is an absence (AL/SL/UL/OL/PH — already the case before this fix).
public class ResignedSuggestionTests
{
    private const string Code = "DE~99999~Leavetown~Weg 1";
    private const string Name = "Leavetown";
    private static readonly DateOnly Date = DateOnly.FromDateTime(DateTime.Today);

    private static DbContextOptions<GSDContext> Options() =>
        new DbContextOptionsBuilder<GSDContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static GSDContext NewDb() => new(Options());

    // ── SubstituteEligibility (the shared rule) ──────────────────────────────────

    private static ShiftEntry Resigned(string emp, DateOnly date) =>
        new() { EmployeeId = emp, ShiftDate = date, ShiftType = SubstituteEligibility.Resigned };

    [Fact]
    public void EarliestResignedByEmployee_TakesTheEarliestDate()
    {
        var map = SubstituteEligibility.EarliestResignedByEmployee(new[]
        {
            new ShiftEntry { EmployeeId = "E1", ShiftDate = Date.AddDays(-2), ShiftType = "AL" },
            Resigned("E1", Date.AddDays(-1)),
            Resigned("E1", Date.AddDays(-9)),
            Resigned("E2", Date),
        });

        Assert.Equal(Date.AddDays(-9), map["E1"]);
        Assert.Equal(Date, map["E2"]);
        Assert.False(map.ContainsKey("E3"));
    }

    [Fact]
    public void ResignedOnOrBefore_IsTrueOnTheDayItself_AndFalseForALaterResignation()
    {
        var map = SubstituteEligibility.EarliestResignedByEmployee(new[] { Resigned("E1", Date) });

        Assert.True(SubstituteEligibility.ResignedOnOrBefore("E1", Date, map));          // the day itself
        Assert.True(SubstituteEligibility.ResignedOnOrBefore("E1", Date.AddDays(3), map)); // after
        Assert.False(SubstituteEligibility.ResignedOnOrBefore("E1", Date.AddDays(-1), map)); // before
        Assert.False(SubstituteEligibility.ResignedOnOrBefore("E9", Date, map));           // unknown
        Assert.False(SubstituteEligibility.ResignedOnOrBefore(null, Date, map));           // no id
    }

    [Fact]
    public void HasLeft_IsTrueForAnInactiveRow_EvenWithoutAnyResignedShiftEntry()
    {
        var none = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);

        Assert.True(SubstituteEligibility.HasLeft(
            new Employee { EmployeeId = "E1", IsActive = false }, Date, none));
        Assert.False(SubstituteEligibility.HasLeft(
            new Employee { EmployeeId = "E2", IsActive = true }, Date, none));
        Assert.False(SubstituteEligibility.HasLeft(null, Date, none));
    }

    // ── Coverage gaps: the banner's "Available: …" floaters ──────────────────────

    /// <summary>A centre that is open on <paramref name="date"/> and short of its minimum.</summary>
    private static void SeedGapCentre(GSDContext db, DateOnly date)
    {
        db.WicLocations.Add(new WicLocation
        {
            Id = 1, LocationCode = Code, DisplayName = Name, City = "Leavetown",
            MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = (int)date.DayOfWeek, OpenTime = "08:00", CloseTime = "17:00",
        });
        // The centre's own MAIN agent — no WIC cover that day, so the day is a gap.
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = Code, EmployeeName = "Main Agent", AssignmentType = "MAIN", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "M1", FullName = "Main Agent", IsActive = true });
        // A horizon row so the roster cap does not shorten the requested window.
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "M1", ShiftDate = date.AddDays(20), ShiftType = ShiftTypes.Working, RawValue = "BO",
        });
    }

    private static void SeedFloater(GSDContext db, string empId, string fullName, bool isActive = true)
    {
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = Code, EmployeeName = fullName, AssignmentType = "REGIONAL", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = empId, FullName = fullName, IsActive = isActive });
    }

    private static async Task<WicCoverageGapEntry> SingleGap(GSDContext db)
    {
        var result = await new WicCoverageGapService(db).GetGapsAsync(1);
        return Assert.Single(result.Gaps);
    }

    [Fact]
    public async Task AvailableFloater_ResignedOnTheGapDay_IsNotOffered()
    {
        using var db = NewDb();
        SeedGapCentre(db, Date);
        SeedFloater(db, "F1", "Leaver");
        db.ShiftEntries.Add(Resigned("F1", Date));
        await db.SaveChangesAsync();

        var gap = await SingleGap(db);

        Assert.Empty(gap.AvailableFloaters);
    }

    [Fact]
    public async Task AvailableFloater_ResignedBeforeTheGapDay_IsNotOffered()
    {
        // The live 9130648 shape: the RESIGNED row is on an earlier date and the gap day
        // itself has no row at all, so a same-day-only check would still publish them.
        using var db = NewDb();
        SeedGapCentre(db, Date);
        SeedFloater(db, "F1", "Leaver");
        db.ShiftEntries.Add(Resigned("F1", Date.AddDays(-5)));
        await db.SaveChangesAsync();

        var gap = await SingleGap(db);

        Assert.Empty(gap.AvailableFloaters);
    }

    [Fact]
    public async Task AvailableFloater_InactiveEmployee_IsNotOffered()
    {
        using var db = NewDb();
        SeedGapCentre(db, Date);
        SeedFloater(db, "F1", "Leaver", isActive: false);
        await db.SaveChangesAsync();

        var gap = await SingleGap(db);

        Assert.Empty(gap.AvailableFloaters);
    }

    [Theory]
    [InlineData("AL")]
    [InlineData("SL")]
    [InlineData("UL")]
    [InlineData("OL")]
    [InlineData("PH")]
    public async Task AvailableFloater_OnLeaveThatDay_IsNotOffered(string leaveType)
    {
        using var db = NewDb();
        SeedGapCentre(db, Date);
        SeedFloater(db, "F1", "Leaver");
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "F1", ShiftDate = Date, ShiftType = leaveType });
        await db.SaveChangesAsync();

        var gap = await SingleGap(db);

        Assert.Empty(gap.AvailableFloaters);
    }

    [Fact]
    public async Task AvailableFloater_WorkingActiveRegionalAgent_IsStillOffered()
    {
        // Control: the fix must not empty the hint list for a genuinely available floater.
        using var db = NewDb();
        SeedGapCentre(db, Date);
        SeedFloater(db, "F1", "Floater");
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "F1", ShiftDate = Date, ShiftType = ShiftTypes.Working });
        await db.SaveChangesAsync();

        var gap = await SingleGap(db);

        var floater = Assert.Single(gap.AvailableFloaters);
        Assert.Equal("F1", floater.EmployeeId);
    }

    // ── Assign candidates (the Assign picker on a coverage-gap row) ──────────────

    private static void SeedAssignCentre(GSDContext db)
    {
        db.WicLocations.Add(new WicLocation { LocationCode = Code, DisplayName = Name, IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = Code, EmployeeName = "Main Agent", AssignmentType = "MAIN", IsActive = true });
    }

    // The resigned/inactive ASSIGN-picker cases live in AssignCandidateFilterTests.cs (the
    // 2026-10-16 filter: history up to the asked day, future exit dates, IsOnDemand kept,
    // the /api/employees date parameter and the Shift Plan window).

    [Fact]
    public async Task AssignCandidates_LeaveOnTheDay_StaysListedButUnavailable()
    {
        // Pinned existing behaviour: AL/SL/… candidates stay visible (greyed out with the
        // reason), only leavers and inactive rows disappear entirely.
        using var db = NewDb();
        SeedAssignCentre(db);
        db.Employees.Add(new Employee { EmployeeId = "A1", FullName = "On Leave", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "A1", ShiftDate = Date, ShiftType = "AL" });
        await db.SaveChangesAsync();

        var result = await new WicAssignCandidatesService(db).GetCandidatesAsync(Code, Date);

        var row = Assert.Single(result!.Candidates, c => c.EmployeeId == "A1");
        Assert.False(row.Available);
        Assert.Equal("AL", row.AbsenceReason);
    }

    // ── The other candidate lists that offer an agent for WIC cover ──────────────

    [Fact]
    public async Task VwicCandidates_Leaver_IsNeverAnAvailableAgent()
    {
        // This endpoint's own isAbsent test deliberately does not list RESIGNED, so a
        // resigned Voice agent used to come back as an offerable candidate.
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = "V1", FullName = "Voice Here", PrimaryRole = "Voice", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "V2", FullName = "Voice Gone", PrimaryRole = "Voice", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "V3", FullName = "Voice On Leave", PrimaryRole = "Voice", IsActive = true });
        db.ShiftEntries.Add(Resigned("V2", Date.AddDays(-5)));
        // Other leave that day: this endpoint's old inline isAbsent list omitted OL.
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "V3", ShiftDate = Date, ShiftType = "OL" });
        await db.SaveChangesAsync();

        var svc = new VwicService(db);

        var withDate = await svc.GetCandidatesAsync(Date.ToString("yyyy-MM-dd"));
        Assert.Contains("V1", withDate.Select(c => c.EmployeeId));
        Assert.DoesNotContain("V2", withDate.Select(c => c.EmployeeId));
        Assert.DoesNotContain("V3", withDate.Select(c => c.EmployeeId));

        var noDate = await svc.GetCandidatesAsync(null);
        Assert.Contains("V1", noDate.Select(c => c.EmployeeId));
        Assert.DoesNotContain("V2", noDate.Select(c => c.EmployeeId));
    }

    [Fact]
    public async Task BackupCandidates_Leaver_IsNotOffered()
    {
        using var db = NewDb();
        db.WicLocations.Add(new WicLocation
        {
            LocationCode = Code, DisplayName = Name, City = "Leavetown",
            MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = (int)Date.DayOfWeek, OpenTime = "08:00", CloseTime = "17:00",
        });
        foreach (var (id, name) in new[] { ("B1", "Backup Gone"), ("B2", "Backup Here") })
        {
            db.WicAgentAssignments.Add(new WicAgentAssignment
            { LocationCode = Code, EmployeeName = name, AssignmentType = "BACKUP", IsActive = true });
            db.Employees.Add(new Employee { EmployeeId = id, FullName = name, IsActive = true });
            // A qualifying WORKING row on the day, so only the resignation can exclude B1.
            db.ShiftEntries.Add(new ShiftEntry { EmployeeId = id, ShiftDate = Date, ShiftType = ShiftTypes.Working });
        }
        db.ShiftEntries.Add(Resigned("B1", Date.AddDays(-7)));
        await db.SaveChangesAsync();

        var result = await new BackupService(db).GetBackupAsync(Code, Date.ToString("yyyy-MM-dd"), 1);

        var day = Assert.Single(result!.Days);
        Assert.True(day.IsAtRisk);
        var ids = day.Candidates.Select(c => c.EmployeeId).ToList();
        Assert.Contains("B2", ids);       // control: a real backup is still offered
        Assert.DoesNotContain("B1", ids); // left a week before the day
    }

    // ── Find Substitute / Best substitute (SubstitutionService) ──────────────────

    private static ServiceProvider NewProvider(string name)
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));
        sc.AddDbContext<GSDContext>(o => o.UseInMemoryDatabase(name));
        sc.AddScoped<ReachabilityService>();
        sc.AddScoped<CoverageEvaluator>();
        return sc.BuildServiceProvider();
    }

    [Fact]
    public async Task SubstituteCandidates_Leavers_AreNeverOffered_ButWorkingAgentsAre()
    {
        var name = Guid.NewGuid().ToString();
        using var provider = NewProvider(name);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GSDContext>();

        db.WicLocations.Add(new WicLocation
        {
            LocationCode = Code, DisplayName = Name, City = "Leavetown",
            MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = (int)Date.DayOfWeek, OpenTime = "08:00", CloseTime = "17:00",
        });
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = Code, EmployeeName = "Main Agent", AssignmentType = "MAIN", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "M1", FullName = "Main Agent", IsActive = true });

        // SSP pool: one healthy, one resigned before the day, one resigned on the day.
        db.Employees.Add(new Employee { EmployeeId = "S1", FullName = "Backlog One", PrimaryRole = "SSP", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "S2", FullName = "Backlog Gone", PrimaryRole = "SSP", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "S3", FullName = "Backlog Today", PrimaryRole = "SSP", IsActive = true });
        db.ShiftEntries.Add(Resigned("S2", Date.AddDays(-5)));
        db.ShiftEntries.Add(Resigned("S3", Date));
        await db.SaveChangesAsync();

        var svc = new SubstitutionService(
            db, new ReachabilityService(provider.GetRequiredService<IServiceScopeFactory>()),
            new CoverageEvaluator(db), NullLogger<SubstitutionService>.Instance);

        var result = await svc.GetSubstitutesAsync(Code, Date.ToString("yyyy-MM-dd"), 1);

        Assert.NotNull(result);
        var day = Assert.Single(result!.Days);
        var ids = day.Candidates.Select(c => c.EmployeeId).ToList();
        Assert.Contains("S1", ids);      // still offered
        Assert.DoesNotContain("S2", ids); // left before the day
        Assert.DoesNotContain("S3", ids); // left on the day
        Assert.Equal("S1", day.BestPickId);
    }
}
