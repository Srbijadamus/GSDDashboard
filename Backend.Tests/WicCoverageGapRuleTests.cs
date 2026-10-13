using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// Rule 2 (2026-10-13 Overview fix). THE rule this endpoint must obey:
//
//   a centre that is OPEN that day (WicHoursResolver, manual exceptions included) and
//   has FEWER agents than MinRequired is a GAP.
//
// The banner and the Coverage Risk list both read that number from this one endpoint,
// so it must be a per-day fact and never a location-level summary inside the next
// 14 days. "Record-keeping" (NOT_RECORDED) may only describe open days BEYOND that
// 14-day horizon and only when the location has no plan at all out there.
//
// Live bug this pins (2026-10-13): Demmin - Woldeforster Str and Wesel were open and
// uncovered on every open day of the 14-day window, but the old 7-day cutoff saw their
// only cover evidence inside the cutoff, summarised both locations as NOT_RECORDED,
// and the banner stayed green while the list showed the same days as UNCOVERED.
public class WicCoverageGapRuleTests
{
    private const string Code = "DE~99999~Ruleheim~Regelweg 1";
    private const string Name = "Ruleheim";

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static WicLocation Loc(int minRequired = 1) => new()
    {
        Id = 1, LocationCode = Code, DisplayName = Name, City = "Ruleheim",
        MinAgentsRequired = minRequired, IsActive = true,
    };

    private static void SeedCentre(GSDContext db, DateOnly date, int minRequired = 1)
    {
        db.WicLocations.Add(Loc(minRequired));
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = (int)date.DayOfWeek, OpenTime = "08:00", CloseTime = "17:00",
        });
        db.WicAgentAssignments.Add(new WicAgentAssignment
        {
            LocationCode = Code, EmployeeName = "Rule Main", AssignmentType = "MAIN", IsActive = true,
        });
        db.Employees.Add(new Employee { EmployeeId = "R1", FullName = "Rule Main", IsActive = true });
    }

    private static ShiftEntry WicDuty(DateOnly date) => new()
    {
        EmployeeId = "R1", ShiftDate = date, ShiftType = ShiftTypes.WicDuty,
        AgentTask = Name, ShiftStart = "08:00", ShiftEnd = "17:00", IsWicDuty = true,
    };

    [Fact]
    public async Task OpenCentre_NoCoverInWindow_IsAGapEveryOpenDay_NotRecordKeeping()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(14);

        Assert.Empty(result.NotRecorded);            // never record-keeping inside 14 days
        Assert.Equal(2, result.Gaps.Count);          // today and today+7, the open weekdays
        Assert.All(result.Gaps, g =>
        {
            Assert.Equal(0, g.Present);
            Assert.Equal(1, g.Required);
        });
    }

    [Fact]
    public async Task OpenCentre_CoveredEarlyButNotLater_StillListsTheLaterGap()
    {
        // The exact shape that silenced the banner: a duty row on an early day of the
        // window must not turn the location into a single "no records" summary.
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        db.ShiftEntries.Add(WicDuty(today));
        // Push the roster horizon past the window, so `to` is not clamped back to today.
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "R1", ShiftDate = today.AddDays(20),
            ShiftType = ShiftTypes.Working, RawValue = "BO",
        });
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(14);

        Assert.Empty(result.NotRecorded);
        var gap = Assert.Single(result.Gaps);
        Assert.Equal(today.AddDays(7).ToString("yyyy-MM-dd"), gap.Date);
        Assert.Equal(0, gap.Present);
        Assert.Equal(1, gap.Required);
    }

    [Fact]
    public async Task OpenCentre_AtMinRequired_HasNoGaps()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        db.ShiftEntries.Add(WicDuty(today));
        db.ShiftEntries.Add(WicDuty(today.AddDays(7)));
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(14);

        Assert.Empty(result.Gaps);
        Assert.Empty(result.NotRecorded);
    }

    [Fact]
    public async Task OpenCentre_BelowMinRequired_IsAGap_NotOnlyWhenNobodyIsThere()
    {
        // "fewer agents than MinRequired": one present against a minimum of two is a gap.
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today, minRequired: 2);
        db.ShiftEntries.Add(WicDuty(today));
        db.ShiftEntries.Add(WicDuty(today.AddDays(7)));
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(14);

        Assert.Empty(result.NotRecorded);
        Assert.Equal(2, result.Gaps.Count);
        Assert.All(result.Gaps, g =>
        {
            Assert.Equal(1, g.Present);
            Assert.Equal(2, g.Required);
        });
    }

    [Fact]
    public async Task RecordKeeping_IsOnlyUsedBeyondTheFourteenDayHorizon()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(28);

        // Days 0..13 are always gaps (today, today+7). The far open days with no plan at
        // all (today+14, today+21) are the only ones the record-keeping bucket may hold.
        Assert.Equal(2, result.Gaps.Count);
        var summary = Assert.Single(result.NotRecorded);
        Assert.Equal("NOT_RECORDED", summary.Reason);
        Assert.Equal(2, summary.OpenDaysInWindow);
    }

    [Fact]
    public async Task ClosedCentre_IsNeverAGap()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        db.WicLocations.Add(Loc());
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = (int)today.DayOfWeek, IsClosed = true,
        });
        db.WicAgentAssignments.Add(new WicAgentAssignment
        {
            LocationCode = Code, EmployeeName = "Rule Main", AssignmentType = "MAIN", IsActive = true,
        });
        db.Employees.Add(new Employee { EmployeeId = "R1", FullName = "Rule Main", IsActive = true });
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(14);

        Assert.Empty(result.Gaps);
        Assert.Empty(result.NotRecorded);
    }
}
