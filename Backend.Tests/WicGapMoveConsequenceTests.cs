using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// Owner decision 2026-10-16. The "Available" column on a WIC coverage gap
// (WicCoverageGapService.BuildGap, Overview.tsx) STAYS A HINT FOR RTM: nobody is hidden
// and nothing is assigned automatically. What is added is only the CONSEQUENCE of the
// move — for each proposed agent, the state of THEIR OWN centre afterwards:
// present−1 against that centre's MinRequired, measured only over the part of the day
// the gap and the source centre's working hours share (WicHoursResolver).
//
//   * agent alone in the source centre            -> "WIC <centre> → 0/1" warning
//   * source centre stays at or above Min         -> no warning
//   * source centre closed at the gap time        -> no warning (nothing to lose there)
//   * BO day / "rostered, no task"                -> no warning (no centre at all)
//   * the proposal list itself is unchanged       -> same agents, same order, same count
public class WicGapMoveConsequenceTests
{
    private const string GapCode = "DE~99999~Gapheim~Gapweg 1";
    private const string GapName = "Gapheim";
    private const string SrcCode = "DE~88888~Quellheim~Quellweg 2";
    private const string SrcName = "Quellheim";

    // Widened to the whole day so the gap centre and the source centre share their window.
    private static readonly DateOnly Date = DateOnly.FromDateTime(DateTime.Today);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>An open centre, short of its minimum, whose only own agent is not on site.</summary>
    private static void SeedGapCentre(GSDContext db, int minRequired = 1)
    {
        db.WicLocations.Add(new WicLocation
        {
            Id = 1, LocationCode = GapCode, DisplayName = GapName, City = "Gapheim",
            MinAgentsRequired = minRequired, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = GapCode, DayOfWeek = (int)Date.DayOfWeek, OpenTime = "08:00", CloseTime = "17:00",
        });
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = GapCode, EmployeeName = "Gap Main", AssignmentType = "MAIN", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "M1", FullName = "Gap Main", IsActive = true });
        // Horizon row so the roster cap does not shorten the requested window.
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "M1", ShiftDate = Date.AddDays(20), ShiftType = ShiftTypes.Working, RawValue = "BO",
        });
    }

    /// <summary>The centre a proposed floater is actually on duty at, with its own hours.</summary>
    private static void SeedSourceCentre(
        GSDContext db, int minRequired = 1, string? open = "08:00", string? close = "17:00", bool closed = false)
    {
        db.WicLocations.Add(new WicLocation
        {
            Id = 2, LocationCode = SrcCode, DisplayName = SrcName, City = "Quellheim",
            MinAgentsRequired = minRequired, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = SrcCode, DayOfWeek = (int)Date.DayOfWeek, IsClosed = closed,
            OpenTime = closed ? null : open, CloseTime = closed ? null : close,
        });
    }

    /// <summary>A REGIONAL agent shared with the gap centre, on duty at <paramref name="agentTask"/>.</summary>
    private static void SeedFloater(
        GSDContext db, string empId, string fullName,
        string? agentTask = SrcName, string shiftType = ShiftTypes.WicDuty,
        string? rawValue = null, string? manualChangeBy = null)
    {
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = GapCode, EmployeeName = fullName, AssignmentType = "REGIONAL", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = empId, FullName = fullName, IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = empId, ShiftDate = Date, ShiftType = shiftType, AgentTask = agentTask,
            RawValue = rawValue, ManualChangeBy = manualChangeBy, ShiftStart = "08:00", ShiftEnd = "17:00",
        });
    }

    private static async Task<WicCoverageGapEntry> GapFor(GSDContext db, string code = GapCode)
    {
        var result = await new WicCoverageGapService(db).GetGapsAsync(1);
        return Assert.Single(result.Gaps, g => g.LocationCode == code);
    }

    // ── 1. the agent is the only cover of their own centre ───────────────────────

    [Fact]
    public async Task Floater_AloneInTheSourceCentre_IsWarnedAbout0of1()
    {
        using var db = NewDb();
        SeedGapCentre(db);
        SeedSourceCentre(db);
        SeedFloater(db, "F1", "Floater One");
        await db.SaveChangesAsync();

        var gap = await GapFor(db);
        var floater = Assert.Single(gap.AvailableFloaters);

        Assert.Equal("F1", floater.EmployeeId);
        Assert.Equal($"WIC {SrcName}", floater.TaskLabel);
        Assert.Equal(SrcName, floater.SourceLocation);
        Assert.Equal(0, floater.SourcePresentAfter);
        Assert.Equal(1, floater.SourceRequired);
        Assert.True(floater.SourceBelowMinimum);
    }

    [Fact]
    public async Task Floater_AloneButSourceCentreNeedsTwo_IsWarnedAbout0of2()
    {
        using var db = NewDb();
        SeedGapCentre(db);
        SeedSourceCentre(db, minRequired: 2);
        SeedFloater(db, "F1", "Floater One");
        await db.SaveChangesAsync();

        var floater = Assert.Single((await GapFor(db)).AvailableFloaters);

        Assert.Equal(0, floater.SourcePresentAfter);
        Assert.Equal(2, floater.SourceRequired);
        Assert.True(floater.SourceBelowMinimum);
    }

    // ── 2. the source centre stays at or above its minimum ───────────────────────

    [Fact]
    public async Task Floater_SourceCentreKeepsItsMinimum_IsNotWarned()
    {
        using var db = NewDb();
        SeedGapCentre(db);
        SeedSourceCentre(db);
        SeedFloater(db, "F1", "Floater One");
        // A second, independent duty at the source centre (no WIC assignment of his own):
        // after the move the centre still has 1 of 1.
        db.Employees.Add(new Employee { EmployeeId = "S2", FullName = "Quell Helper", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "S2", ShiftDate = Date, ShiftType = ShiftTypes.WicDuty,
            AgentTask = SrcName, ShiftStart = "08:00", ShiftEnd = "17:00",
        });
        await db.SaveChangesAsync();

        var floater = Assert.Single((await GapFor(db)).AvailableFloaters);

        Assert.Equal(SrcName, floater.SourceLocation);
        Assert.Equal(1, floater.SourcePresentAfter);
        Assert.Equal(1, floater.SourceRequired);
        Assert.False(floater.SourceBelowMinimum);
    }

    // ── 3. the source centre is closed at the gap time ───────────────────────────

    [Fact]
    public async Task Floater_SourceCentreClosedThatDay_IsNotWarned()
    {
        using var db = NewDb();
        SeedGapCentre(db);
        SeedSourceCentre(db, closed: true);
        SeedFloater(db, "F1", "Floater One");
        await db.SaveChangesAsync();

        var floater = Assert.Single((await GapFor(db)).AvailableFloaters);

        Assert.False(floater.SourceBelowMinimum);
        Assert.Null(floater.SourcePresentAfter);
    }

    [Fact]
    public async Task Floater_SourceCentreOpenOutsideTheGapWindow_IsNotWarned()
    {
        // Open, but 06:00-07:00 against the gap's 08:00-17:00: no shared minute, so the
        // move cannot make this centre short while the gap needs cover.
        using var db = NewDb();
        SeedGapCentre(db);
        SeedSourceCentre(db, open: "06:00", close: "07:00");
        SeedFloater(db, "F1", "Floater One");
        await db.SaveChangesAsync();

        var floater = Assert.Single((await GapFor(db)).AvailableFloaters);

        Assert.Equal(SrcName, floater.SourceLocation);
        Assert.False(floater.SourceBelowMinimum);
        Assert.Null(floater.SourcePresentAfter);
    }

    // ── 4. BO / "rostered, no task" have no centre to lose ───────────────────────

    [Fact]
    public async Task Floater_OnBackOfficeThatDay_IsNotWarned()
    {
        using var db = NewDb();
        SeedGapCentre(db);
        SeedSourceCentre(db);
        SeedFloater(db, "F1", "Floater Bo",
            agentTask: null, shiftType: ShiftTypes.Working, rawValue: "BO", manualChangeBy: "TEST_FIXTURE");
        await db.SaveChangesAsync();

        var floater = Assert.Single((await GapFor(db)).AvailableFloaters);

        Assert.Equal("BO", floater.TaskLabel);
        Assert.Null(floater.SourceLocation);
        Assert.False(floater.SourceBelowMinimum);
    }

    [Fact]
    public async Task Floater_RosteredWithNoTask_IsNotWarned()
    {
        using var db = NewDb();
        SeedGapCentre(db);
        SeedSourceCentre(db);
        SeedFloater(db, "F1", "Floater NoTask",
            agentTask: null, shiftType: ShiftTypes.Working, rawValue: null, manualChangeBy: null);
        await db.SaveChangesAsync();

        var floater = Assert.Single((await GapFor(db)).AvailableFloaters);

        Assert.Equal("rostered, no task", floater.TaskLabel);
        Assert.False(floater.SourceBelowMinimum);
        Assert.Null(floater.SourcePresentAfter);
    }

    [Fact]
    public async Task Floater_AlreadyOnDutyAtTheGapCentre_IsNotWarned()
    {
        // Pulling them "here" is not a move: no consequence to report. (The centre is
        // still a gap because it needs two and the floater is only the one present.)
        using var db = NewDb();
        SeedGapCentre(db, minRequired: 2);
        SeedSourceCentre(db);
        SeedFloater(db, "F1", "Floater Local", agentTask: GapName);
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(1);
        var gap = Assert.Single(result.Gaps, g => g.LocationCode == GapCode);

        var floater = Assert.Single(gap.AvailableFloaters);
        Assert.Equal($"WIC {GapName}", floater.TaskLabel);
        Assert.False(floater.SourceBelowMinimum);
        Assert.Null(floater.SourceLocation);
    }

    // ── 5. the proposal list itself is untouched ─────────────────────────────────

    [Fact]
    public async Task ProposalList_KeepsTheSameAgentsInTheSameOrder_OnlyAddsTheConsequence()
    {
        using var db = NewDb();
        SeedGapCentre(db);
        SeedSourceCentre(db);
        SeedFloater(db, "F2", "Floater Zeta");                                   // alone -> warned
        SeedFloater(db, "F1", "Floater Alpha", agentTask: null,
            shiftType: ShiftTypes.Working);                                      // no task -> not warned
        SeedFloater(db, "F3", "Floater Mid", agentTask: null,
            shiftType: ShiftTypes.Working, rawValue: "BO", manualChangeBy: "TEST_FIXTURE"); // BO -> not warned
        await db.SaveChangesAsync();

        var gap = await GapFor(db);

        // Same agents, same count, same FullName order as before the change.
        Assert.Equal(new[] { "F1", "F3", "F2" }, gap.AvailableFloaters.Select(a => a.EmployeeId).ToArray());
        Assert.Equal(3, gap.AvailableFloaters.Count);

        // Only the one whose own centre would fall short carries the warning.
        Assert.False(gap.AvailableFloaters[0].SourceBelowMinimum);
        Assert.False(gap.AvailableFloaters[1].SourceBelowMinimum);
        Assert.True(gap.AvailableFloaters[2].SourceBelowMinimum);
        Assert.Equal(0, gap.AvailableFloaters[2].SourcePresentAfter);
        Assert.Equal(1, gap.AvailableFloaters[2].SourceRequired);
    }

    [Fact]
    public async Task OwnAgents_AreNeverWarned_TheConsequenceBelongsToTheAvailableHintOnly()
    {
        using var db = NewDb();
        SeedGapCentre(db);
        SeedSourceCentre(db);
        SeedFloater(db, "F1", "Floater One");
        await db.SaveChangesAsync();

        var gap = await GapFor(db);
        var own = Assert.Single(gap.AssignedAgents);

        Assert.Null(own.SourceLocation);
        Assert.False(own.SourceBelowMinimum);
    }
}
