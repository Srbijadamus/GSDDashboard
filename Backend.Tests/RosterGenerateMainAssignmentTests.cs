using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace GSDDashboard.Tests;

// The roster generator must NEVER silently write a permanent MAIN WIC assignment.
// The old block ("the coverage view only lists agents with an active
// WicAgentAssignments row and no screen can create one — add the MAIN row if it
// is missing", RosterService.cs:687-709) made "Generate roster" an implicit,
// permanent change of an agent's home centre. The rules pinned here:
//   1. an agent who is MAIN at ANOTHER centre keeps that centre: the shifts for
//      the selected period are still generated, the MAIN row is untouched, and
//      the caller gets "MAIN_UNCHANGED: agent already has main centre X" — even
//      when confirmCreateMain is passed;
//   2. an agent with NO MAIN at all gets one only with the explicit
//      confirmCreateMain flag (the UI confirmation dialog "This will set
//      <centre> as the agent's permanent main WIC"); without it the shifts are
//      generated and no assignment row is written.
public class RosterGenerateMainAssignmentTests
{
    private const string MainName    = "Main Agent";
    private const string NoMainName  = "No Main Agent";
    private const string MainEmp     = "E-MAIN";
    private const string NoMainEmp   = "E-NOMAIN";

    // 2026-10-05 is a Monday, 2026-10-09 a Friday.
    private const string Mon = "2026-10-05";
    private const string Fri = "2026-10-09";

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RosterService Svc(GSDContext db) =>
        new(db, new WicShiftService(db, new AvailabilityResolver(db)), new WicCoverageGapService(db));

    // WIC mode requires the agent's own shift (RosterService.ValidateAsync): the
    // centre's opening hours are never substituted for it. Both centres here are
    // open 08:00-17:00, so this is the agent's shift, cut to that window per centre.
    private static RosterRequest Req(string emp, string locationCode, bool confirmCreateMain) =>
        new(emp, Mon, Fri, "08:00", "17:00", null, null, locationCode, false, confirmCreateMain);

    private static void Seed(GSDContext db)
    {
        db.Employees.Add(new Employee { EmployeeId = MainEmp,   FullName = MainName,   IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = NoMainEmp, FullName = NoMainName, IsActive = true });

        // Centre A carries a legacy bridge code so the "already MAIN here" match
        // covers both LocationCode and LocationCodeLegacy (WicLocationMatcher).
        db.WicLocations.Add(new WicLocation
        { LocationCode = "A", DisplayName = "Centre A", LocationCodeLegacy = "DE_A", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "B", DisplayName = "Centre B", IsActive = true });

        // Both centres open every weekday, so every weekday in the range is a WIC day.
        for (var dow = 1; dow <= 5; dow++)
        {
            db.WicOpeningHours.Add(new WicOpeningHour
            { LocationCode = "A", DayOfWeek = dow, OpenTime = "08:00", CloseTime = "17:00", IsClosed = false });
            db.WicOpeningHours.Add(new WicOpeningHour
            { LocationCode = "B", DayOfWeek = dow, OpenTime = "08:00", CloseTime = "17:00", IsClosed = false });
        }
    }

    // ── Case 1: MAIN at another centre ───────────────────────────────────────

    [Fact]
    public async Task Generate_AgentMainAtAnotherCentre_GeneratesShifts_ButNeverMovesMain()
    {
        using var db = NewDb();
        Seed(db);
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = "A", EmployeeName = MainName, AssignmentType = "MAIN", IsActive = true });
        await db.SaveChangesAsync();

        // ConfirmCreateMain=true is deliberately passed: rule 1 wins over the flag.
        var g = await Svc(db).GenerateAsync(Req(MainEmp, "B", confirmCreateMain: true), "K1", "Tester");

        Assert.False(g.AssignmentCreated);
        Assert.Equal("unchanged", g.MainAction);
        Assert.Equal("MAIN_UNCHANGED: agent already has main centre Centre A", g.MainWarning);
        Assert.Equal("Centre A", g.MainCentreName);

        // The shifts for the selected period WERE generated — at the selected centre.
        Assert.Equal(5, g.Created);
        Assert.Equal(5, g.WicDays);
        var rows = await db.ShiftEntries.Where(s => s.EmployeeId == MainEmp).ToListAsync();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.Equal("Centre B", r.AgentTask));

        // ...and the permanent MAIN row is byte-for-byte what it was.
        var only = Assert.Single(await db.WicAgentAssignments.ToListAsync());
        Assert.Equal("A", only.LocationCode);
        Assert.Equal(MainName, only.EmployeeName);
        Assert.Equal("MAIN", only.AssignmentType);
        Assert.True(only.IsActive);
    }

    [Fact]
    public async Task Preview_AgentMainAtAnotherCentre_ReportsUnchangedWarning_BeforeGenerating()
    {
        using var db = NewDb();
        Seed(db);
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = "A", EmployeeName = MainName, AssignmentType = "MAIN", IsActive = true });
        await db.SaveChangesAsync();

        var p = await Svc(db).PreviewAsync(Req(MainEmp, "B", confirmCreateMain: false));

        Assert.False(p.WouldCreateMain);              // no confirmation dialog for case 1
        Assert.Equal("A", p.ExistingMainCode);
        Assert.Equal("Centre A", p.ExistingMainName);
        Assert.Equal("MAIN_UNCHANGED: agent already has main centre Centre A", p.MainWarning);
    }

    // ── Case 2: no MAIN at all ───────────────────────────────────────────────

    [Fact]
    public async Task Generate_AgentWithoutMain_AndNoConfirm_GeneratesShifts_ButNoMain()
    {
        using var db = NewDb();
        Seed(db);
        await db.SaveChangesAsync();

        // The preview tells the UI that confirming would set the MAIN...
        var p = await Svc(db).PreviewAsync(Req(NoMainEmp, "A", confirmCreateMain: false));
        Assert.True(p.WouldCreateMain);
        Assert.Null(p.ExistingMainCode);
        Assert.Contains("MAIN_NOT_CREATED", p.MainWarning);

        // ...but without the flag the generate writes shifts only.
        var g = await Svc(db).GenerateAsync(Req(NoMainEmp, "A", confirmCreateMain: false), "K1", "Tester");

        Assert.False(g.AssignmentCreated);
        Assert.Equal("not_created", g.MainAction);
        Assert.Contains("MAIN_NOT_CREATED", g.MainWarning);
        Assert.Equal("Centre A", g.MainCentreName);
        Assert.Equal(5, g.Created);
        Assert.Equal(5, g.WicDays);

        Assert.Empty(await db.WicAgentAssignments.ToListAsync());
        Assert.Equal(5, await db.ShiftEntries.CountAsync(s => s.EmployeeId == NoMainEmp));
    }

    [Fact]
    public async Task Generate_AgentWithoutMain_WithConfirm_CreatesMainOnce()
    {
        using var db = NewDb();
        Seed(db);
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(Req(NoMainEmp, "A", confirmCreateMain: true), "K1", "Tester");

        Assert.True(g.AssignmentCreated);
        Assert.Equal("created", g.MainAction);
        Assert.Null(g.MainWarning);
        Assert.Equal("Centre A", g.MainCentreName);
        Assert.Equal(5, g.Created);

        var only = Assert.Single(await db.WicAgentAssignments.ToListAsync());
        Assert.Equal("A", only.LocationCode);
        Assert.Equal(NoMainName, only.EmployeeName);
        Assert.Equal("MAIN", only.AssignmentType);
        Assert.True(only.IsActive);
    }

    // ── Boundary cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task Generate_AgentAlreadyMainHere_MatchesLegacyCode_DoesNotDuplicate()
    {
        using var db = NewDb();
        Seed(db);
        // Stored with the LEGACY bridge code: still "MAIN for Centre A".
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = "DE_A", EmployeeName = MainName, AssignmentType = "MAIN", IsActive = true });
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(Req(MainEmp, "A", confirmCreateMain: true), "K1", "Tester");

        Assert.False(g.AssignmentCreated);
        Assert.Equal("already_main", g.MainAction);
        Assert.Null(g.MainWarning);
        var only = Assert.Single(await db.WicAgentAssignments.ToListAsync());
        Assert.Equal("DE_A", only.LocationCode);
    }

    [Fact]
    public async Task Generate_InactiveMain_DoesNotCountAsMain_AndNeedsConfirm()
    {
        using var db = NewDb();
        Seed(db);
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = "A", EmployeeName = MainName, AssignmentType = "MAIN", IsActive = false });
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(Req(MainEmp, "A", confirmCreateMain: true), "K1", "Tester");

        Assert.True(g.AssignmentCreated);
        Assert.Equal("created", g.MainAction);
        Assert.Equal(2, await db.WicAgentAssignments.CountAsync());
        Assert.Single(await db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync());
    }

    [Fact]
    public async Task Generate_PlainRoster_NeverTouchesMain_EvenWithTheFlag()
    {
        using var db = NewDb();
        Seed(db);
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(NoMainEmp, Mon, Fri, "08:00", "16:30", null, null, null, false, true),
            "K1", "Tester");

        Assert.Null(g.MainAction);
        Assert.Null(g.MainWarning);
        Assert.Null(g.MainCentreName);
        Assert.False(g.AssignmentCreated);
        Assert.Empty(await db.WicAgentAssignments.ToListAsync());
    }
}
