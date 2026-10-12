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

// Agents with MORE THAN ONE active MAIN WIC assignment are a correct state, not an
// error: live data has 7 such agents (e.g. Ion Bodnariuc = Halle Mon/Tue +
// Markkleeberg Wed/Thu, Mahboubeh Abdighara = Osnabrück Mon + Münster Tue +
// Recklinghausen Wed). When the RTM generates a roster with NO LocationCode, the
// generator must follow WicOpeningHours DAY BY DAY and put the agent in the MAIN
// centre that is open on that date — not lock the whole period to whichever MAIN
// row SQL happened to return first (RosterService.ValidateAsync, the
// FirstOrDefaultAsync with no ORDER BY).
//
// The rules pinned here:
//   1. per-day auto-detection: each generated WIC day carries the centre open that
//      day (ShiftEntries.AgentTask + the matching WicShiftEntries row);
//   2. a temporary manual move to another centre changes nothing permanent — the
//      next generator run restores the day-by-day MAIN rotation;
//   3. a day where two MAIN centres of the same agent are BOTH open is the only
//      genuine ambiguity and is reported (never silently guessed);
//   4. MAIN_UNCHANGED names every MAIN centre of the agent, not just the first.
public class RosterMultiMainPerDayTests
{
    private const string Emp  = "E-MULTIMAIN";
    private const string Name = "Multi Main Agent";

    // 2026-10-05 is a Monday, 2026-10-11 a Sunday.
    private const string WeekFrom = "2026-10-05";
    private const string WeekTo   = "2026-10-11";

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RosterService Svc(GSDContext db) =>
        new(db, new WicShiftService(db, new AvailabilityResolver(db)), new WicCoverageGapService(db));

    private static void SeedTwoMainCentres(GSDContext db)
    {
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "A", DisplayName = "Centre A", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "B", DisplayName = "Centre B", IsActive = true });

        // Disjoint opening days: A on Monday + Tuesday, B on Wednesday + Thursday.
        int[] aDays = { (int)DayOfWeek.Monday, (int)DayOfWeek.Tuesday };
        int[] bDays = { (int)DayOfWeek.Wednesday, (int)DayOfWeek.Thursday };
        foreach (var d in aDays)
            db.WicOpeningHours.Add(new WicOpeningHour
            { LocationCode = "A", DayOfWeek = d, OpenTime = "08:00", CloseTime = "12:00", IsClosed = false });
        foreach (var d in bDays)
            db.WicOpeningHours.Add(new WicOpeningHour
            { LocationCode = "B", DayOfWeek = d, OpenTime = "13:00", CloseTime = "16:00", IsClosed = false });

        db.WicAgentAssignments.Add(new WicAgentAssignment
        { Id = 10, LocationCode = "A", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { Id = 11, LocationCode = "B", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
    }

    // ── 1. Auto-detection follows the opening hours day by day ───────────────

    [Fact]
    public async Task Generate_NoLocationCode_TwoMainCentres_PlacesAgentInTheCentreOpenThatDay()
    {
        using var db = NewDb();
        SeedTwoMainCentres(db);
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, WeekFrom, WeekTo, null, null, null, null), "K1", "Tester");

        Assert.Equal(5, g.Created);      // Mon, Tue, Wed, Thu, Fri
        Assert.Equal(4, g.WicDays);
        Assert.Equal(1, g.BoDays);       // Friday — every MAIN centre is closed
        Assert.False(g.AssignmentCreated);

        var expected = new (string Date, string Centre)[]
        {
            ("2026-10-05", "Centre A"), // Monday    — A open, B closed
            ("2026-10-06", "Centre A"), // Tuesday   — A open, B closed
            ("2026-10-07", "Centre B"), // Wednesday — B open, A closed
            ("2026-10-08", "Centre B"), // Thursday  — B open, A closed
        };

        foreach (var (date, centre) in expected)
        {
            var d = DateOnly.Parse(date);
            var shift = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == d);
            Assert.Equal(ShiftTypes.WicDuty, shift.ShiftType);
            Assert.True(shift.IsWicDuty);
            Assert.Equal(centre, shift.AgentTask);

            var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == Emp && w.ShiftDate == d);
            Assert.Equal(centre, wic.SupportLocation);
            Assert.True(wic.IsOnSite);
            Assert.Equal(centre == "Centre A" ? "08:00-12:00" : "13:00-16:00", wic.WorkingShift);
        }

        // Both centres stayed MAIN: the run never wrote, moved or duplicated an assignment.
        var mains = await db.WicAgentAssignments.Where(a => a.IsActive && a.AssignmentType == "MAIN").ToListAsync();
        Assert.Equal(2, mains.Count);
        Assert.Contains(mains, m => m.LocationCode == "A");
        Assert.Contains(mains, m => m.LocationCode == "B");
    }

    // The centre the agent actually works is readable from the preview, day by day.
    [Fact]
    public async Task Preview_NoLocationCode_TwoMainCentres_ReportsTheCentrePerRow()
    {
        using var db = NewDb();
        SeedTwoMainCentres(db);
        await db.SaveChangesAsync();

        var p = await Svc(db).PreviewAsync(
            new RosterRequest(Emp, WeekFrom, WeekTo, null, null, null, null));

        Assert.Equal(5, p.RowCount);
        Assert.Equal(4, p.WicDays);
        Assert.Equal(1, p.BoDays);
        Assert.All(p.Rows.Where(r => r.Kind == "WIC"), r => Assert.False(string.IsNullOrEmpty(r.LocationCode)));
        // A BO day names no centre: the first MAIN centre is closed that day.
        Assert.All(p.Rows.Where(r => r.Kind == "BO"), r => Assert.True(string.IsNullOrEmpty(r.LocationCode)));
        Assert.Equal("A", p.Rows.Single(r => r.Date == "2026-10-05").LocationCode);
        Assert.Equal("B", p.Rows.Single(r => r.Date == "2026-10-07").LocationCode);

        // Not ambiguous: no day has two open MAIN centres for this agent.
        Assert.Empty(p.MainOverlapDays!);
    }

    // ── 2. A temporary move never disturbs the day-by-day rotation ───────────

    [Fact]
    public async Task TemporaryMove_ToAnotherMain_RestoresSameRotationOnTheNextGenerate()
    {
        using var db = NewDb();
        SeedTwoMainCentres(db);
        await db.SaveChangesAsync();

        // The agent is moved to Centre A for Wednesday + Thursday of the first week
        // (the days Centre B is open) — a deliberate, temporary manual change.
        var wicSvc = new WicShiftService(db, new AvailabilityResolver(db));
        foreach (var date in new[] { "2026-10-07", "2026-10-08" })
        {
            var res = await wicSvc.CreateAssignmentAsync(
                new CreateAssignmentRequest(Emp, "A", date, "08:00", "12:00"),
                db, manualActor: "RTM-KID");
            Assert.False(res is Microsoft.AspNetCore.Http.HttpResults.BadRequest);
        }

        // Next week: the generator (no LocationCode, no Overwrite) puts the agent back
        // into the MAIN centre that is open on each day — the rotation is unchanged.
        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, "2026-10-12", "2026-10-18", null, null, null, null), "K1", "Tester");

        Assert.Equal(5, g.Created);
        Assert.Equal(4, g.WicDays);
        Assert.Equal(1, g.BoDays);   // Friday

        foreach (var (date, centre) in new (string, string)[]
        {
            ("2026-10-12", "Centre A"), // Monday
            ("2026-10-13", "Centre A"), // Tuesday
            ("2026-10-14", "Centre B"), // Wednesday
            ("2026-10-15", "Centre B"), // Thursday
        })
        {
            var d = DateOnly.Parse(date);
            var shift = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == d);
            Assert.Equal(centre, shift.AgentTask);
            var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == Emp && w.ShiftDate == d);
            Assert.Equal(centre, wic.SupportLocation);
        }

        // ...and the manual rows of the previous week were not rewritten.
        var wed = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == new DateOnly(2026, 10, 7));
        Assert.Equal("Centre A", wed.AgentTask);

        Assert.Equal(2, await db.WicAgentAssignments.CountAsync(a => a.IsActive && a.AssignmentType == "MAIN"));
    }

    // ── 3. Two open MAIN centres on one date are the only genuine ambiguity ──
    //
    // Centre C is open Monday AND Wednesday, Centre D is open Monday 09:00-14:00 —
    // so Monday has TWO open MAIN centres. Rule (2026-10-12): the centre's hours
    // never pick between them; the generator repeats the last decision for that
    // weekday, and with none it must NOT guess ("first MAIN wins" was the old
    // behaviour). Monday is therefore left WITHOUT a WIC centre (a BO day) and
    // reported in MainAmbiguousDays. Wednesday is unambiguous (only C is
    // open) and stays a WIC day.

    [Fact]
    public async Task Generate_OverlappingMainDays_ReportsTheAmbiguousDay_AndPlacesNobodyThere()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "C", DisplayName = "Centre C", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "D", DisplayName = "Centre D", IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "C", DayOfWeek = (int)DayOfWeek.Monday,    OpenTime = "08:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "C", DayOfWeek = (int)DayOfWeek.Wednesday, OpenTime = "08:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "D", DayOfWeek = (int)DayOfWeek.Monday,    OpenTime = "09:00", CloseTime = "14:00" });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 20, LocationCode = "C", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 21, LocationCode = "D", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        await db.SaveChangesAsync();

        var p = await Svc(db).PreviewAsync(new RosterRequest(Emp, WeekFrom, WeekTo, null, null, null, null));

        // A day where two MAIN centres are open at the SAME TIME is reported; a day
        // where only one of them (or none) is open is not.
        var ambiguous = Assert.Single(p.MainOverlapDays!);
        Assert.Contains("Monday", ambiguous);
        Assert.Contains("Centre C", ambiguous);
        Assert.Contains("Centre D", ambiguous);
        Assert.DoesNotContain(p.MainOverlapDays!, d => d.Contains("Wednesday"));

        // The ambiguous DAY itself is reported per date and carries no centre.
        var day = Assert.Single(p.MainAmbiguousDays!);
        Assert.StartsWith("2026-10-05 (Monday):", day);
        Assert.Contains("Centre C", day);
        Assert.Contains("Centre D", day);
        Assert.Equal("BO", p.Rows.Single(r => r.Date == "2026-10-05").Kind);
        Assert.Null(p.Rows.Single(r => r.Date == "2026-10-05").LocationCode);

        // The run still creates a row per day — the ambiguity is reported, not fatal.
        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, WeekFrom, WeekTo, null, null, null, null), "K1", "Tester");
        Assert.Equal(5, g.Created);   // Mon, Tue, Wed, Thu, Fri
        Assert.Equal(1, g.WicDays);   // Wednesday only — Monday is NOT guessed
        Assert.Equal(4, g.BoDays);    // Monday, Tuesday, Thursday, Friday
        var sameAmbiguity = Assert.Single(g.MainOverlapDays!);
        Assert.Contains("Monday", sameAmbiguity);
        Assert.Single(g.MainAmbiguousDays!);

        // Monday got a ShiftEntries row but no WicShiftEntries row at all.
        var monday = DateOnly.Parse("2026-10-05");
        Assert.False(await db.WicShiftEntries.AnyAsync(w => w.EmployeeId == Emp && w.ShiftDate == monday));
        var wednesdayShift = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == DateOnly.Parse("2026-10-07"));
        Assert.Equal("Centre C", wednesdayShift.AgentTask);
    }

    // ── 4. MAIN_UNCHANGED names EVERY MAIN centre, not only the first ────────

    [Fact]
    public async Task Generate_ExplicitOtherLocation_ListsEveryMainCentreInTheWarning()
    {
        using var db = NewDb();
        SeedTwoMainCentres(db);
        db.WicLocations.Add(new WicLocation { LocationCode = "Z", DisplayName = "Centre Z", IsActive = true });
        for (var d = 1; d <= 5; d++)
            db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "Z", DayOfWeek = d, OpenTime = "08:00", CloseTime = "17:00" });
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, WeekFrom, WeekTo, null, null, null, "Z"), "K1", "Tester");

        Assert.Equal("unchanged", g.MainAction);
        Assert.NotNull(g.MainWarning);
        Assert.Contains("Centre A", g.MainWarning);
        Assert.Contains("Centre B", g.MainWarning);

        // Nothing was created for Centre Z: the two MAIN rows are untouched.
        Assert.False(g.AssignmentCreated);
        Assert.Equal(2, await db.WicAgentAssignments.CountAsync());
    }

    // A single-MAIN agent keeps the exact warning text the UI contract pins
    // (RosterGenerateMainAssignmentTests) — the multi-centre list is additive.
    [Fact]
    public async Task Generate_ExplicitOtherLocation_SingleMain_KeepsTheExactWarning()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "A", DisplayName = "Centre A", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "B", DisplayName = "Centre B", IsActive = true });
        for (var d = 1; d <= 5; d++)
        {
            db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "A", DayOfWeek = d, OpenTime = "08:00", CloseTime = "17:00" });
            db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "B", DayOfWeek = d, OpenTime = "08:00", CloseTime = "17:00" });
        }
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = "A", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, WeekFrom, WeekTo, null, null, null, "B"), "K1", "Tester");

        Assert.Equal("MAIN_UNCHANGED: agent already has main centre Centre A", g.MainWarning);
    }
}
