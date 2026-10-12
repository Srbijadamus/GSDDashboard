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

// A day on which TWO OR MORE MAIN centres of the same agent are OPEN.
//
// Rule (2026-10-12, replaces the old "rule 2b"): the centre's working hours NEVER
// determine the centre or the agent's shift. They only say WHICH MAIN centres are
// open. When exactly one is open, that centre is used (the centre's own window is
// the historical fallback when the request carries no agent shift). When two or more
// are open, the LAST DECISION for that weekday is repeated EXACTLY as it was — the
// same centre or centres, with the same times — taken from the most recent earlier
// date with that weekday that has on-site WicShiftEntries rows (earlier dates of the
// same run count too). With no such decision the day is left without a centre and
// reported as MAIN_AMBIGUOUS; it is never guessed.
//
// The Demmin live shape these tests mirror: "Demmin - Am Hanseufer" Tue/Thu
// 10:00-12:00 + "Demmin - Woldeforster Str" Tue/Thu 13:00-15:00, both MAIN for
// Rene Altmeyer. Centre hours are NOT the agent's shift: the day-level ShiftEntries
// row carries the decision's own span and each centre's row its own decided slice.
public class RosterMultiMainSameDayTests
{
    private const string Emp  = "E-SAMEDAY";
    private const string Name = "Same Day Agent";

    // 2026-10-08 is a Thursday; 2026-10-01 and 2026-09-24 are the two Thursdays before.
    // 2026-10-05 is a Monday; 2026-09-28 and 2026-09-14 the Mondays before it.
    private static readonly DateOnly Thursday      = new(2026, 10, 8);
    private static readonly DateOnly PrevThursday  = new(2026, 10, 1);
    private static readonly DateOnly Prev2Thursday = new(2026, 9, 24);
    private static readonly DateOnly Monday        = new(2026, 10, 5);
    private static readonly DateOnly PrevMonday    = new(2026, 9, 28);
    private static readonly DateOnly Prev2Monday   = new(2026, 9, 14);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RosterService Svc(GSDContext db) =>
        new(db, new WicShiftService(db, new AvailabilityResolver(db)), new WicCoverageGapService(db));

    // A 10:00-12:00 and B 13:00-15:00, both open on Thursday only, both MAIN.
    private static void SeedTwoThursdayCentres(GSDContext db)
    {
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "A", DisplayName = "Centre A", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "B", DisplayName = "Centre B", IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "A", DayOfWeek = (int)DayOfWeek.Thursday, OpenTime = "10:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "B", DayOfWeek = (int)DayOfWeek.Thursday, OpenTime = "13:00", CloseTime = "15:00" });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 10, LocationCode = "A", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 11, LocationCode = "B", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
    }

    // The RTM's recorded decision on one earlier date: a WicShiftEntries row per centre.
    private static void SeedDecision(GSDContext db, DateOnly date, params (string Centre, string Shift)[] rows)
    {
        foreach (var (centre, shift) in rows)
            db.WicShiftEntries.Add(new WicShiftEntry
            {
                EmployeeId = Emp, ShiftDate = date, DayOfWeek = date.DayOfWeek.ToString(),
                SupportLocation = centre, WorkingShift = shift, IsOnSite = true, Task = "WIC",
            });
    }

    // ── no earlier decision: MAIN_AMBIGUOUS, nobody placed ───────────────────

    [Fact]
    public async Task TwoOpenMainCentres_NoEarlierDecision_IsAmbiguousAndPlacesNobody()
    {
        using var db = NewDb();
        SeedTwoThursdayCentres(db);
        await db.SaveChangesAsync();

        var p = await Svc(db).PreviewAsync(
            new RosterRequest(Emp, "2026-10-08", "2026-10-08", null, null, null, null));

        var overlap = Assert.Single(p.MainOverlapDays!);
        Assert.Contains("Thursday", overlap);
        Assert.Contains("Centre A", overlap);
        Assert.Contains("Centre B", overlap);

        var day = Assert.Single(p.MainAmbiguousDays!);
        Assert.StartsWith("2026-10-08 (Thursday):", day);
        Assert.Contains("Centre A", day);
        Assert.Contains("Centre B", day);
        Assert.Equal("BO", Assert.Single(p.Rows).Kind);

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, "2026-10-08", "2026-10-08", null, null, null, null), "K1", "Tester");

        Assert.Equal(1, g.Created);
        Assert.Equal(0, g.WicDays);
        Assert.Equal(1, g.BoDays);
        Assert.Single(g.MainAmbiguousDays!);
        Assert.False(await db.WicShiftEntries.AnyAsync(w => w.EmployeeId == Emp));
        Assert.Empty(await db.ShiftEntries.Where(s => s.EmployeeId == Emp && s.IsWicDuty).ToListAsync());
    }

    // ── 2+ open: the last decision is repeated verbatim ──────────────────────

    [Fact]
    public async Task TwoOpenMainCentres_RepeatsTheLastDecision_SameCentresAndSameTimes()
    {
        using var db = NewDb();
        SeedTwoThursdayCentres(db);
        // The RTM's decision last Thursday — deliberately NOT the centre windows.
        SeedDecision(db, PrevThursday, ("Centre A", "09:00-11:00"), ("Centre B", "12:00-14:00"));
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, "2026-10-08", "2026-10-08", null, null, null, null), "K1", "Tester");

        Assert.Equal(1, g.WicDays);
        Assert.Empty(g.MainAmbiguousDays!);

        // The day row spans the repeated decision (09:00-14:00), never the centre
        // windows (10:00-12:00 + 13:00-15:00).
        var shift = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == Thursday);
        Assert.Equal(ShiftTypes.WicDuty, shift.ShiftType);
        Assert.Equal("09:00", shift.ShiftStart);
        Assert.Equal("14:00", shift.ShiftEnd);

        var rows = await db.WicShiftEntries.Where(w => w.EmployeeId == Emp && w.ShiftDate == Thursday).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.SupportLocation == "Centre A" && r.WorkingShift == "09:00-11:00" && r.IsOnSite);
        Assert.Contains(rows, r => r.SupportLocation == "Centre B" && r.WorkingShift == "12:00-14:00" && r.IsOnSite);
    }

    [Fact]
    public async Task TwoOpenMainCentres_Preview_ReportsTheRepeatedDecisionPerCentre()
    {
        using var db = NewDb();
        SeedTwoThursdayCentres(db);
        SeedDecision(db, PrevThursday, ("Centre A", "09:00-11:00"), ("Centre B", "12:00-14:00"));
        await db.SaveChangesAsync();

        var p = await Svc(db).PreviewAsync(
            new RosterRequest(Emp, "2026-10-08", "2026-10-08", null, null, null, null));

        var row = Assert.Single(p.Rows);
        Assert.Equal("WIC", row.Kind);
        Assert.Equal("09:00", row.ShiftStart);
        Assert.Equal("14:00", row.ShiftEnd);
        Assert.NotNull(row.Centres);
        Assert.Equal(2, row.Centres!.Count);
        Assert.Contains(row.Centres, c => c.DisplayName == "Centre A" && c.Start == "09:00" && c.End == "11:00");
        Assert.Contains(row.Centres, c => c.DisplayName == "Centre B" && c.Start == "12:00" && c.End == "14:00");
    }

    [Fact]
    public async Task TwoOpenMainCentres_OverlappingWindows_RepeatTheDecisionNotTheHours()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "C", DisplayName = "Centre C", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "D", DisplayName = "Centre D", IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "C", DayOfWeek = (int)DayOfWeek.Monday, OpenTime = "08:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "D", DayOfWeek = (int)DayOfWeek.Monday, OpenTime = "09:00", CloseTime = "14:00" });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 20, LocationCode = "C", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 21, LocationCode = "D", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        // Last Monday: the agent was on site at Centre D 07:00-13:00.
        SeedDecision(db, PrevMonday, ("Centre D", "07:00-13:00"));
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, "2026-10-05", "2026-10-05", null, null, null, null), "K1", "Tester");

        Assert.Empty(g.MainAmbiguousDays!);
        Assert.Equal(1, g.WicDays);

        var monday = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == Monday);
        Assert.Equal("Centre D", monday.AgentTask);
        var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == Emp && w.ShiftDate == Monday);
        Assert.Equal("Centre D", wic.SupportLocation);
        Assert.Equal("07:00-13:00", wic.WorkingShift);   // the decision, not D's 09:00-14:00
    }

    // The most recent week wins: an older Monday decision must not override a newer one.
    [Fact]
    public async Task TwoOpenMainCentres_UsesTheNewestWeekThatHasADecision()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "C", DisplayName = "Centre C", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "D", DisplayName = "Centre D", IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "C", DayOfWeek = (int)DayOfWeek.Monday, OpenTime = "08:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "D", DayOfWeek = (int)DayOfWeek.Monday, OpenTime = "09:00", CloseTime = "14:00" });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 20, LocationCode = "C", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 21, LocationCode = "D", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        // Older: C. Newer: D. The generator must repeat D.
        SeedDecision(db, Prev2Monday, ("Centre C", "08:00-12:00"));
        SeedDecision(db, PrevMonday,  ("Centre D", "09:00-14:00"));
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, "2026-10-05", "2026-10-05", null, null, null, null), "K1", "Tester");

        Assert.Equal(1, g.WicDays);
        var monday = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == Monday);
        Assert.Equal("Centre D", monday.AgentTask);
    }

    // The last decision names a centre that is not an open MAIN centre today: the day
    // is ambiguous. An OLDER decision must NOT be resurrected — that would be a guess.
    [Fact]
    public async Task TwoOpenMainCentres_LastDecisionUnusable_DoesNotFallBackToAnOlderOne()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "C", DisplayName = "Centre C", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "D", DisplayName = "Centre D", IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "C", DayOfWeek = (int)DayOfWeek.Monday, OpenTime = "08:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "D", DayOfWeek = (int)DayOfWeek.Monday, OpenTime = "09:00", CloseTime = "14:00" });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 20, LocationCode = "C", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 21, LocationCode = "D", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        SeedDecision(db, Prev2Monday, ("Centre D", "09:00-14:00"));            // older, usable
        SeedDecision(db, PrevMonday,  ("Some Other Centre", "09:00-14:00"));   // newer, unusable
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, "2026-10-05", "2026-10-05", null, null, null, null), "K1", "Tester");

        Assert.Equal(0, g.WicDays);
        Assert.Equal(1, g.BoDays);
        Assert.Single(g.MainAmbiguousDays!);
        Assert.False(await db.WicShiftEntries.AnyAsync(w => w.EmployeeId == Emp && w.ShiftDate == Monday));
    }

    // A multi-week PREVIEW writes nothing, so the decision of an earlier date of the
    // SAME run must be repeated later in that run — including its times.
    [Fact]
    public async Task TwoOpenMainCentres_LaterWeekOfTheSameRun_RepeatsTheEarlierWeekOfThatRun()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "A", DisplayName = "Centre A", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "B", DisplayName = "Centre B", IsActive = true });
        // A is open every Tuesday. B is open only from the SECOND Tuesday of the run.
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "A", DayOfWeek = (int)DayOfWeek.Tuesday, OpenTime = "08:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "B", DayOfWeek = (int)DayOfWeek.Tuesday, OpenTime = "13:00", CloseTime = "15:00", EffectiveFrom = new DateOnly(2026, 10, 13) });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 10, LocationCode = "A", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 11, LocationCode = "B", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        await db.SaveChangesAsync();

        var p = await Svc(db).PreviewAsync(
            new RosterRequest(Emp, "2026-10-06", "2026-10-13", null, null, null, null));

        // 2026-10-06: only A open -> A with A's own window.
        var first = p.Rows.Single(r => r.Date == "2026-10-06");
        Assert.Equal("WIC", first.Kind);
        Assert.Equal("A", first.LocationCode);

        // 2026-10-13: A and B open -> repeat 10-06's decision (A 08:00-12:00), and NOT
        // a guess between A and B.
        var second = p.Rows.Single(r => r.Date == "2026-10-13");
        Assert.Equal("WIC", second.Kind);
        Assert.Equal("A", second.LocationCode);
        Assert.Equal("08:00", second.ShiftStart);
        Assert.Null(second.Centres);
        Assert.Empty(p.MainAmbiguousDays!);
    }
}
