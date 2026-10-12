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

// A day on which TWO OR MORE MAIN centres of the same agent are open.
//
//   b) windows do NOT overlap → the agent may cover BOTH centres; the WorkingShift
//      of each WicShiftEntries row is the part of the AGENT's shift (ShiftStart-
//      ShiftEnd) that falls inside that centre's own window. A centre the agent's
//      shift never touches is not assigned that day.
//   c) windows DO overlap → the day cannot be read from WicOpeningHours. The centre
//      the agent actually held on the same weekday in the most recent earlier week
//      that has a WIC row wins (the RTM's latest decision). With no such decision the
//      day is left without a centre and reported as MAIN_AMBIGUOUS — never guessed.
//
// The Demmin live shape these tests mirror: "Demmin - Am Hanseufer" Tue/Thu
// 10:00-12:00 + "Demmin - Woldeforster Str" Tue/Thu 13:00-15:00, both MAIN for
// Rene Altmeyer. Centre hours are NOT the agent's shift: the day-level ShiftEntries
// row carries the agent's own shift and each centre's row carries only its slice.
public class RosterMultiMainSameDayTests
{
    private const string Emp  = "E-SAMEDAY";
    private const string Name = "Same Day Agent";

    // 2026-10-08 is a Thursday; 2026-10-05 a Monday.
    private static readonly DateOnly Thursday = new(2026, 10, 8);
    private static readonly DateOnly Monday   = new(2026, 10, 5);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RosterService Svc(GSDContext db) =>
        new(db, new WicShiftService(db, new AvailabilityResolver(db)), new WicCoverageGapService(db));

    // A 10:00-12:00 and B 13:00-15:00, both open on Thursday only, both MAIN.
    private static void SeedDisjointThursdayCentres(GSDContext db)
    {
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "A", DisplayName = "Centre A", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "B", DisplayName = "Centre B", IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "A", DayOfWeek = (int)DayOfWeek.Thursday, OpenTime = "10:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "B", DayOfWeek = (int)DayOfWeek.Thursday, OpenTime = "13:00", CloseTime = "15:00" });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 10, LocationCode = "A", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 11, LocationCode = "B", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
    }

    // ── b) disjoint windows: the agent covers both ───────────────────────────

    [Fact]
    public async Task DisjointMainWindows_AgentShiftCoversBoth_AgentGetsBothCentres()
    {
        using var db = NewDb();
        SeedDisjointThursdayCentres(db);
        await db.SaveChangesAsync();

        var req = new RosterRequest(Emp, "2026-10-08", "2026-10-08", "10:00", "15:00", null, null);
        var g = await Svc(db).GenerateAsync(req, "K1", "Tester");

        Assert.Equal(1, g.Created);
        Assert.Empty(g.MainAmbiguousDays!);

        // The day row is the AGENT's shift — not a centre's opening hours.
        var shift = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == Thursday);
        Assert.Equal(ShiftTypes.WicDuty, shift.ShiftType);
        Assert.Equal("10:00", shift.ShiftStart);
        Assert.Equal("15:00", shift.ShiftEnd);

        // One WicShiftEntries row per centre, each carrying that centre's slice of the
        // agent's shift.
        var rows = await db.WicShiftEntries.Where(w => w.EmployeeId == Emp && w.ShiftDate == Thursday).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.SupportLocation == "Centre A" && r.WorkingShift == "10:00-12:00" && r.IsOnSite);
        Assert.Contains(rows, r => r.SupportLocation == "Centre B" && r.WorkingShift == "13:00-15:00" && r.IsOnSite);
    }

    [Fact]
    public async Task DisjointMainWindows_Preview_ReportsBothCentresWithTheirSlices()
    {
        using var db = NewDb();
        SeedDisjointThursdayCentres(db);
        await db.SaveChangesAsync();

        var p = await Svc(db).PreviewAsync(
            new RosterRequest(Emp, "2026-10-08", "2026-10-08", "10:00", "15:00", null, null));

        var row = Assert.Single(p.Rows);
        Assert.Equal("WIC", row.Kind);
        Assert.Equal("10:00", row.ShiftStart);
        Assert.Equal("15:00", row.ShiftEnd);
        Assert.NotNull(row.Centres);
        Assert.Equal(2, row.Centres!.Count);
        Assert.Contains(row.Centres, c => c.DisplayName == "Centre A" && c.Start == "10:00" && c.End == "12:00");
        Assert.Contains(row.Centres, c => c.DisplayName == "Centre B" && c.Start == "13:00" && c.End == "15:00");
    }

    [Fact]
    public async Task DisjointMainWindows_AgentShiftMissesOneCentre_ThatCentreIsNotAssigned()
    {
        using var db = NewDb();
        SeedDisjointThursdayCentres(db);
        await db.SaveChangesAsync();

        // 08:00-11:00 touches Centre A's 10:00-12:00 window (slice 10:00-11:00) but
        // never touches Centre B's 13:00-15:00 window.
        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, "2026-10-08", "2026-10-08", "08:00", "11:00", null, null), "K1", "Tester");

        Assert.Equal(1, g.Created);
        var rows = await db.WicShiftEntries.Where(w => w.EmployeeId == Emp && w.ShiftDate == Thursday).ToListAsync();
        var only = Assert.Single(rows);
        Assert.Equal("Centre A", only.SupportLocation);
        Assert.Equal("10:00-11:00", only.WorkingShift);
        Assert.DoesNotContain(rows, r => r.SupportLocation == "Centre B");
    }

    [Fact]
    public async Task DisjointMainWindows_AgentShiftTouchesNoCentre_IsABoDay()
    {
        using var db = NewDb();
        SeedDisjointThursdayCentres(db);
        await db.SaveChangesAsync();

        // 06:00-08:00 is before both windows: the agent covers neither centre, so the
        // day is BO — no WIC row is invented.
        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, "2026-10-08", "2026-10-08", "06:00", "08:00", null, null), "K1", "Tester");

        Assert.Equal(1, g.Created);
        Assert.Equal(0, g.WicDays);
        Assert.Equal(1, g.BoDays);
        Assert.Empty(await db.WicShiftEntries.Where(w => w.EmployeeId == Emp).ToListAsync());
    }

    // ── c) overlapping windows: follow the last same-weekday decision ─────────

    [Fact]
    public async Task OverlappingMainWindows_FollowsTheMostRecentSameWeekdayDecision()
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

        // The RTM's latest Monday decision: the agent was on site at Centre D on the
        // previous Monday (2026-09-28). That is what the overlapping Monday follows.
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = Emp, ShiftDate = new DateOnly(2026, 9, 28), DayOfWeek = "Monday",
            SupportLocation = "Centre D", WorkingShift = "09:00-14:00", IsOnSite = true, Task = "WIC",
        });
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, "2026-10-05", "2026-10-09", null, null, null, null), "K1", "Tester");

        Assert.Empty(g.MainAmbiguousDays!);
        Assert.Equal(2, g.WicDays);   // Monday -> D (last decision) + Wednesday -> C

        var monday = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == Monday);
        Assert.Equal("Centre D", monday.AgentTask);
        var mondayWic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == Emp && w.ShiftDate == Monday);
        Assert.Equal("Centre D", mondayWic.SupportLocation);
        Assert.Equal("09:00-14:00", mondayWic.WorkingShift);

        var wednesday = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == new DateOnly(2026, 10, 7));
        Assert.Equal("Centre C", wednesday.AgentTask);
    }

    // The most recent week wins: an older Monday decision must not override a newer one.
    [Fact]
    public async Task OverlappingMainWindows_UsesTheNewestWeekThatHasARow()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "C", DisplayName = "Centre C", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "D", DisplayName = "Centre D", IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "C", DayOfWeek = (int)DayOfWeek.Monday, OpenTime = "08:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "D", DayOfWeek = (int)DayOfWeek.Monday, OpenTime = "09:00", CloseTime = "14:00" });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 20, LocationCode = "C", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 21, LocationCode = "D", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });

        // Older: C. Newer: D. The generator must follow D.
        db.WicShiftEntries.Add(new WicShiftEntry { EmployeeId = Emp, ShiftDate = new DateOnly(2026, 9, 14), SupportLocation = "Centre C", IsOnSite = true });
        db.WicShiftEntries.Add(new WicShiftEntry { EmployeeId = Emp, ShiftDate = new DateOnly(2026, 9, 28), SupportLocation = "Centre D", IsOnSite = true });
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            new RosterRequest(Emp, "2026-10-05", "2026-10-05", null, null, null, null), "K1", "Tester");

        Assert.Equal(1, g.WicDays);
        var monday = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == Monday);
        Assert.Equal("Centre D", monday.AgentTask);
    }
}
