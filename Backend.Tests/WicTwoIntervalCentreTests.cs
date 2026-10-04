using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// RTM decision 2026-10-01: at a centre with a second opening interval
// (OpenTime2/CloseTime2) the gap between the intervals is only a break. WIC duty must
// span both intervals, and the break must never count as a coverage hole.
//
// Affected centres: Augsburg, Essenbach, Hannover, München, Quickborn. Augsburg's real
// Thursday hours (07:30-12:00 + 12:30-16:00) are used throughout: 480 required minutes,
// with the 30-minute break belonging to neither interval.
//
// Representation: ONE ShiftEntries row spanning first open -> last close. Not a
// preference — ShiftEntries has three unique indexes that each cap it at one row per
// agent-day (UQ_ShiftEntries_EmpDate (EmployeeId, ShiftDate), ...EmpDateType,
// ...EmpDateSheet) and carries a single ShiftStart/ShiftEnd pair, so a second row cannot
// be inserted at all. See WicHoursResolver.DutyWindow.
public class WicTwoIntervalCentreTests
{
    private const string Code = "DE~86150~Augsburg~Schaezlerstr. 3";
    private const string Name = "Augsburg";

    // Augsburg Thursday, verbatim from WicOpeningHours.
    private static WicOpeningHour TwoIntervalThursday() => new()
    {
        LocationCode = Code, DayOfWeek = 4,
        OpenTime = "07:30", CloseTime = "12:00",
        OpenTime2 = "12:30", CloseTime2 = "16:00",
        RawSchedule = "7:30 -12:00 12:30 - 16:00",
    };

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static RosterService NewSvc(GSDContext db) =>
        new(db, new WicShiftService(db, new AvailabilityResolver(db)), new WicCoverageGapService(db));

    // ── WicHoursResolver.DutyWindow — the one definition ────────────────────────

    [Fact]
    public void DutyWindow_TwoIntervals_SpansBreak_FirstOpenToLastClose()
    {
        var (start, end) = WicHoursResolver.DutyWindow(TwoIntervalThursday());
        Assert.Equal("07:30", start);
        Assert.Equal("16:00", end);   // was "12:00" — duty stopped at the first close
    }

    [Fact]
    public void DutyWindow_SingleInterval_IsUnchanged()
    {
        var (start, end) = WicHoursResolver.DutyWindow(new WicOpeningHour
        {
            LocationCode = "X", DayOfWeek = 4, OpenTime = "09:00", CloseTime = "17:00",
        });
        Assert.Equal("09:00", start);
        Assert.Equal("17:00", end);
    }

    [Fact]
    public void DutyWindow_ClosedOrBlank_YieldsNothing_SoCallerKeepsItsFallback()
    {
        Assert.Equal((null, null), WicHoursResolver.DutyWindow(null));
        Assert.Equal((null, null), WicHoursResolver.DutyWindow(new WicOpeningHour
        {
            LocationCode = "X", DayOfWeek = 4, IsClosed = true,
            OpenTime = "09:00", CloseTime = "17:00",
        }));
        Assert.Equal((null, null), WicHoursResolver.DutyWindow(new WicOpeningHour
        {
            LocationCode = "X", DayOfWeek = 4, OpenTime = "", CloseTime = "",
        }));
    }

    // ── The generator (RosterService.BuildPlanAsync via PreviewAsync) ───────────

    [Fact]
    public async Task Generator_TwoIntervalCentre_WritesDutySpanningBothIntervals()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent E1", IsActive = true });
        db.WicLocations.Add(new WicLocation
        {
            LocationCode = Code, DisplayName = Name, City = Name, MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(TwoIntervalThursday());
        await db.SaveChangesAsync();

        // 2026-10-01 is a Thursday — the only day this centre is seeded open.
        var preview = await NewSvc(db).PreviewAsync(new RosterRequest(
            "E1", "2026-10-01", "2026-10-01", null, null, null, null, Code));

        var row = Assert.Single(preview.Rows);
        Assert.Equal("WIC", row.Kind);
        Assert.Equal("07:30", row.ShiftStart);
        Assert.Equal("16:00", row.ShiftEnd);      // was 12:00 — the afternoon block was unstaffed
        Assert.Equal(1, preview.WicDays);
    }

    [Fact]
    public async Task Generator_SingleIntervalCentre_Unchanged()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent E1", IsActive = true });
        db.WicLocations.Add(new WicLocation
        {
            LocationCode = "DE~X~Single", DisplayName = "Single", MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = "DE~X~Single", DayOfWeek = 4, OpenTime = "09:00", CloseTime = "17:00",
        });
        await db.SaveChangesAsync();

        var preview = await NewSvc(db).PreviewAsync(new RosterRequest(
            "E1", "2026-10-01", "2026-10-01", null, null, null, null, "DE~X~Single"));

        var row = Assert.Single(preview.Rows);
        Assert.Equal("09:00", row.ShiftStart);
        Assert.Equal("17:00", row.ShiftEnd);
    }

    // ── The break is not required time (CoverageCalculator) ────────────────────

    [Fact]
    public void RequiredMinutes_ExcludeTheBreak()
    {
        // 270 + 210 = 480. The 12:00-12:30 break belongs to neither interval, so it is
        // never required and can never read as a hole.
        Assert.Equal(480, CoverageCalculator.CalcOpenMinutes("07:30", "12:00", "12:30", "16:00"));
    }

    [Fact]
    public void AgentSpanningBothIntervals_IsFullyCredited_BreakNotCountedAgainstThem()
    {
        var covered = CoverageCalculator.CalcAgentCoverage("07:30", "16:00", "07:30", "12:00", "12:30", "16:00");
        Assert.Equal(480, covered);

        var result = CoverageCalculator.Calculate(
            false, "07:30", "12:00", "12:30", "16:00",
            new List<(string, string, string?, string?, bool)> { ("E1", "Agent E1", "07:30", "16:00", true) });

        Assert.Equal(480, result.TotalOpenMinutes);
        Assert.Equal(480, result.TotalCoveredMinutes);
        Assert.Equal(100, result.CoveragePercent);
        Assert.Equal(CoverageStatus.COVERED, result.Status);
        Assert.Equal("FULL", Assert.Single(result.Agents).CoverageMatch);
    }

    [Fact]
    public void AgentCoveringOnlyTheFirstInterval_IsStillPartial()
    {
        // The regression guard for the fix: a genuinely missing second interval must NOT
        // be smoothed over into COVERED.
        var result = CoverageCalculator.Calculate(
            false, "07:30", "12:00", "12:30", "16:00",
            new List<(string, string, string?, string?, bool)> { ("E1", "Agent E1", "07:30", "12:00", true) });

        Assert.Equal(480, result.TotalOpenMinutes);
        Assert.Equal(270, result.TotalCoveredMinutes);
        Assert.Equal(56, result.CoveragePercent);
        Assert.Equal(CoverageStatus.PARTIAL, result.Status);
    }

    // ── The Locations page end to end (WicCardsService) ────────────────────────

    private static void SeedCard(GSDContext db, DateOnly date, string shiftStart, string shiftEnd)
    {
        db.WicLocations.Add(new WicLocation
        {
            LocationCode = Code, DisplayName = Name, City = Name, MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(TwoIntervalThursday());
        db.WicAgentAssignments.Add(new WicAgentAssignment
        {
            LocationCode = Code, EmployeeName = "Agent E1", AssignmentType = "MAIN", IsActive = true,
        });
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent E1", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "E1", ShiftDate = date, ShiftType = ShiftTypes.WicDuty,
            AgentTask = Name, ShiftStart = shiftStart, ShiftEnd = shiftEnd, IsWicDuty = true,
        });
    }

    [Fact]
    public async Task Card_SpanningAgent_Is100PercentCovered()
    {
        var date = new DateOnly(2026, 10, 1); // Thursday
        using var db = NewDb();
        SeedCard(db, date, "07:30", "16:00");
        await db.SaveChangesAsync();

        var card = Assert.Single(await new WicCardsService(db).GetCardsAsync(date, null));

        Assert.Equal(480, card.TodaySchedule.TotalOpenMinutes);
        Assert.Equal("COVERED", card.CoverageStatus);
        Assert.Equal(100, card.CoveragePercent);
        Assert.Equal("FULL", Assert.Single(card.AssignedAgents).CoverageMatch);
    }

    [Fact]
    public async Task Card_FirstIntervalOnlyAgent_IsStillPartial()
    {
        var date = new DateOnly(2026, 10, 1);
        using var db = NewDb();
        SeedCard(db, date, "07:30", "12:00");
        await db.SaveChangesAsync();

        var card = Assert.Single(await new WicCardsService(db).GetCardsAsync(date, null));

        Assert.Equal("PARTIAL", card.CoverageStatus);
        Assert.Equal(56, card.CoveragePercent);
    }

    // ── The headcount surfaces are unaffected either way ───────────────────────

    [Fact]
    public async Task Forecast_CountsHeads_SoBothSpanAndFirstIntervalOnlyAreCovered()
    {
        // Deliberate: the forecast/briefing/banner axis is headcount vs MinRequired and
        // does not look at minutes. One rostered head is COVERED whichever window it has;
        // only the card's minute metric distinguishes them. Pinning this stops someone
        // "fixing" the surfaces into agreement by flattening one of the two metrics.
        var date = new DateOnly(2026, 10, 1);
        foreach (var (start, end) in new[] { ("07:30", "16:00"), ("07:30", "12:00") })
        {
            using var db = NewDb();
            SeedCard(db, date, start, end);
            await db.SaveChangesAsync();

            var forecast = await new ForecastService(db).GetForecastAsync(1, Code, date);
            var day = Assert.Single(Assert.Single(forecast.Locations).Forecast);
            Assert.True(day.IsOpen);
            Assert.Equal(1, day.EffectiveCoverage);
            Assert.Equal("COVERED", day.Status);
        }
    }
}
