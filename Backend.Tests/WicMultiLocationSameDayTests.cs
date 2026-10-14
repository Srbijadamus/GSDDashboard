using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// The Demmin dual-centre case (team lead report, 2026-10-08): Rene Altmeyer is PRIMARY
// at "Demmin - Am Hanseufer" (open 10:00-12:00) AND "Demmin - Woldeforster Str" (open
// 13:00-15:00). The two windows do not overlap, so the same agent must be able to cover
// BOTH centres on the same day, and each centre must read as covered.
//
// Data model facts these tests pin (checked against the live database, not assumed):
//   * WicShiftEntries is unique on (EmployeeId, ShiftDate, SupportLocation)
//     — UQ_WicShift_EmpDateLoc — so multiple centres per agent per day are allowed.
//   * ShiftEntries is unique on (EmployeeId, ShiftDate) — UQ_ShiftEntries_EmpDate —
//     so the day-level row can hold only ONE window. That is why the card must read the
//     per-location WicShiftEntries time when an agent holds more than one centre.
public class WicMultiLocationSameDayTests
{
    private static readonly DateOnly Thursday = new(2026, 10, 8); // 2026-10-08 is a Thursday

    private const string CodeA = "DE~17109~ Demmin~Am Hanseufer 2";
    private const string NameA = "Demmin - Am Hanseufer";
    private const string CodeB = "DE~17109~Demmin~Woldeforster Str. 6";
    private const string NameB = "Demmin - Woldeforster Str";

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static void SeedLocations(GSDContext db, string openA = "10:00", string closeA = "12:00",
                                                      string openB = "13:00", string closeB = "15:00")
    {
        db.WicLocations.Add(new WicLocation { LocationCode = CodeA, DisplayName = NameA, City = "Demmin", MinAgentsRequired = 1, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = CodeB, DisplayName = NameB, City = "Demmin", MinAgentsRequired = 1, IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = CodeA, DayOfWeek = (int)Thursday.DayOfWeek, OpenTime = openA, CloseTime = closeA });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = CodeB, DayOfWeek = (int)Thursday.DayOfWeek, OpenTime = openB, CloseTime = closeB });
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = CodeA, EmployeeName = "Rene Altmeyer", AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = CodeB, EmployeeName = "Rene Altmeyer", AssignmentType = "MAIN", IsActive = true });
    }

    private static void SeedEmployee(GSDContext db, string id = "E1", string name = "Rene Altmeyer") =>
        db.Employees.Add(new Employee { EmployeeId = id, FullName = name, IsActive = true });

    private static void SeedOnSiteRow(GSDContext db, string location, string workingShift) =>
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "E1",
            ShiftDate = Thursday,
            DayOfWeek = Thursday.DayOfWeek.ToString(),
            SupportLocation = location,
            WorkingShift = workingShift,
            IsOnSite = true,
            Task = "WIC",
        });

    // ── 1. Creating the second assignment must ADD, not MOVE ─────────────────────

    // The exact live path the team lead uses ("Assign to gap" / "+ New Shift" posts to
    // POST /api/wic/shifts -> WicShiftService.CreateShiftAsync). Before the fix the
    // WicShiftEntries lookup ignored the location, so assigning Woldeforster Str
    // re-pointed Am Hanseufer's only row and Am Hanseufer turned "not on shift".
    [Fact]
    public async Task CreateShift_SecondNonOverlappingCentre_AddsItsOwnRow_AndLeavesTheFirstIntact()
    {
        using var db = NewDb();
        SeedEmployee(db);
        SeedLocations(db);
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        var validation = new ShiftValidationService(db);
        var date = Thursday.ToString("yyyy-MM-dd");

        await svc.CreateShiftAsync(
            new CreateShiftRequest("E1", date, "WIC_DUTY", null, null, null, CodeA), db, validation, "S60028");
        await svc.CreateShiftAsync(
            new CreateShiftRequest("E1", date, "WIC_DUTY", null, null, null, CodeB), db, validation, "S60028");

        var rows = await db.WicShiftEntries.Where(w => w.EmployeeId == "E1").ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.SupportLocation == NameA && r.WorkingShift == "10:00-12:00" && r.IsOnSite);
        Assert.Contains(rows, r => r.SupportLocation == NameB && r.WorkingShift == "13:00-15:00" && r.IsOnSite);

        // Exactly one ShiftEntries row per agent-day is all the schema allows — unchanged.
        Assert.Single(await db.ShiftEntries.Where(s => s.EmployeeId == "E1").ToListAsync());
    }

    // ── 2. Both centres read as covered ─────────────────────────────────────────

    // The day-level ShiftEntries row is deliberately seeded with ONLY Woldeforster's
    // window: that is the shape the live data has after the second assignment (the
    // non-empty ShiftStart guard keeps the first written value). The Am Hanseufer card
    // must still be covered from ITS OWN row's time.
    [Fact]
    public async Task Cards_MultiCentreNonOverlappingDay_BothCentresCovered_WithTheirOwnTimes()
    {
        using var db = NewDb();
        SeedEmployee(db);
        SeedLocations(db);
        SeedOnSiteRow(db, NameA, "10:00-12:00");
        SeedOnSiteRow(db, NameB, "13:00-15:00");
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Thursday, ShiftType = ShiftTypes.WicDuty, IsWicDuty = true,
            AgentTask = NameB, ShiftStart = "13:00", ShiftEnd = "15:00",
        });
        await db.SaveChangesAsync();

        var cards = await new WicCardsService(db).GetCardsAsync(Thursday, null);
        var cardA = Assert.Single(cards, c => c.DisplayName == NameA);
        var cardB = Assert.Single(cards, c => c.DisplayName == NameB);

        Assert.Equal("COVERED", cardA.CoverageStatus);
        Assert.Equal(100, cardA.CoveragePercent);
        var agentA = Assert.Single(cardA.AssignedAgents);
        Assert.Equal("10:00", agentA.ShiftStart);
        Assert.Equal("12:00", agentA.ShiftEnd);
        Assert.Equal("FULL", agentA.CoverageMatch);

        Assert.Equal("COVERED", cardB.CoverageStatus);
        Assert.Equal(100, cardB.CoveragePercent);
        var agentB = Assert.Single(cardB.AssignedAgents);
        Assert.Equal("13:00", agentB.ShiftStart);
        Assert.Equal("15:00", agentB.ShiftEnd);
        Assert.Equal("FULL", agentB.CoverageMatch);
    }

    // ── 3. One assignment: identical to before ──────────────────────────────────

    // Single-centre days deliberately keep the existing reading (the day-level
    // ShiftEntries time, falling back to the row's WorkingShift). This pins that the
    // multi-location fix cannot drift the hundreds of ordinary single-centre cards.
    [Fact]
    public async Task Cards_SingleCentreDay_StillUsesTheDayShiftEntryTime()
    {
        using var db = NewDb();
        SeedEmployee(db);
        SeedLocations(db);
        SeedOnSiteRow(db, NameA, "10:00-12:00");   // row says 10:00-12:00
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Thursday, ShiftType = ShiftTypes.WicDuty, IsWicDuty = true,
            AgentTask = NameA, ShiftStart = "09:00", ShiftEnd = "12:00",   // day row says 09:00-12:00
        });
        await db.SaveChangesAsync();

        var cards = await new WicCardsService(db).GetCardsAsync(Thursday, null);
        var cardA = Assert.Single(cards, c => c.DisplayName == NameA);
        var agent = Assert.Single(cardA.AssignedAgents);
        Assert.Equal("09:00", agent.ShiftStart);
        Assert.Equal("12:00", agent.ShiftEnd);
        Assert.Equal("COVERED", cardA.CoverageStatus);
    }

    // ── 4. Real overlap is still a conflict ────────────────────────────────────

    // The comparison case: the agent's day shift (08:00-16:00) exceeds both centres, and
    // after the 2026-10-14 RTM rule trims each assignment to its centre's window the two
    // trimmed windows (10:00-14:00 / 13:00-15:00) DO overlap, so the agent is
    // double-booked and the day is an OVERLAP.
    [Fact]
    public async Task ConflictDetector_OverlapAfterTrimmingToCentreHours_IsOverlap()
    {
        using var db = NewDb();
        SeedEmployee(db);
        SeedLocations(db, openA: "10:00", closeA: "14:00", openB: "13:00", closeB: "15:00");
        SeedOnSiteRow(db, NameA, "08:00-16:00");
        SeedOnSiteRow(db, NameB, "08:00-16:00");
        await db.SaveChangesAsync();

        var conflicts = await new WicConflictDetector(db).GetConflictsAsync(Thursday, Thursday);

        var conflict = Assert.Single(conflicts);
        Assert.Equal("OVERLAP", conflict.ConflictType);
        Assert.Equal("E1", conflict.EmployeeId);
        Assert.Equal(2, conflict.Locations.Length);
        // Trimmed to the centre windows, not the agent's raw 08:00-16:00.
        Assert.Contains(conflict.Locations, l => l.OpenTime == "10:00" && l.CloseTime == "14:00");
        Assert.Contains(conflict.Locations, l => l.OpenTime == "13:00" && l.CloseTime == "15:00");
    }

    // Two non-overlapping assignments (the Demmin case) are a SPLIT shift, never an
    // OVERLAP: the default conflict list stays empty, the detail view can still show it.
    // The rows carry the agent's wider shift (07:30-16:30) — the detector trims them to
    // the two centre windows (10:00-12:00 / 13:00-15:00) before comparing.
    [Fact]
    public async Task ConflictDetector_NonOverlappingAssignments_IsNotOverlap()
    {
        using var db = NewDb();
        SeedEmployee(db);
        SeedLocations(db);
        SeedOnSiteRow(db, NameA, "07:30-16:30");
        SeedOnSiteRow(db, NameB, "07:30-16:30");
        await db.SaveChangesAsync();

        var detector = new WicConflictDetector(db);

        Assert.Empty(await detector.GetConflictsAsync(Thursday, Thursday));

        var detailed = Assert.Single(await detector.GetConflictsAsync(Thursday, Thursday, includeSplitShifts: true));
        Assert.Equal("SPLIT_SHIFT", detailed.ConflictType);
        Assert.Equal(2, detailed.Locations.Length);
    }
}
