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

    // ── 90-day horizon (restored Overview view, removed in ad4a854) ───────────────
    // The 90-day window must obey the SAME 14-day rule as every other window: the first
    // 14 days are always day-by-day gaps, and beyond them an open centre with no plan at
    // all becomes ONE record-keeping row that is reported separately from the real gaps.
    // Widening the window may never leak record-keeping into the next 14 days, and may
    // never make the banner number disagree with the list: both are result.Gaps.

    [Fact]
    public async Task NinetyDayWindow_KeepsTheFourteenDayRuleAndSeparatesRecordKeeping()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(90);

        // Window really is 90 days from today (the roster cap cannot shorten it: no
        // ShiftEntries rows exist at all, and the endpoint only clamps when it has some).
        Assert.Equal(today.ToString("yyyy-MM-dd"), result.From);
        Assert.Equal(today.AddDays(89).ToString("yyyy-MM-dd"), result.To);

        // Only the open days inside the next 14 days are gaps (day 0 and day 7 here) —
        // never the far ones, and never the record-keeping summary.
        Assert.Equal(2, result.Gaps.Count);
        Assert.All(result.Gaps, g =>
        {
            Assert.Equal(0, g.Present);
            Assert.Equal(1, g.Required);
            Assert.True(DateOnly.Parse(g.Date) <= today.AddDays(13),
                $"{g.Date} is outside the next-14-days gap window");
        });
        Assert.Equal(result.Gaps.Count, result.CriticalCount + result.WarningCount);

        // The far open days with no plan at all are ONE separate record-keeping row.
        var summary = Assert.Single(result.NotRecorded);
        Assert.Equal("NOT_RECORDED", summary.Reason);
        Assert.True(summary.OpenDaysInWindow >= 10, "the 90-day window reaches many open days");

        // Record-keeping never doubles as a gap row.
        Assert.DoesNotContain(result.Gaps, g => g.LocationCode == summary.LocationCode && g.Present > 0);
    }

    [Fact]
    public async Task NinetyDayWindow_AnyCoverBeyondTheCutoff_StopsTheSummaryAndListsTheDays()
    {
        // The 2026-10-13 defect shape at 90 days: cover evidence exists far out, so the
        // location must NOT be summarised as record-keeping — its uncovered days in the
        // gap window belong in the same list the banner counts. The duty sits on
        // today+14 (same weekday), i.e. OUTSIDE the next-14-days gap window, so it makes
        // the far window "has a plan" without covering any of the days at issue.
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        db.ShiftEntries.Add(WicDuty(today.AddDays(14)));
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(90);

        // Roster horizon cap: `to` is the last ShiftEntry date (today+14), not today+89.
        Assert.Equal(today.AddDays(14).ToString("yyyy-MM-dd"), result.To);
        Assert.Empty(result.NotRecorded);
        // Every open day in the window except the covered today+14 is a gap: the two
        // inside the current week (today, today+7) and the third week's today+21 is
        // beyond the window, so 2 gap rows here — the count the banner shows.
        Assert.Equal(2, result.Gaps.Count);
        Assert.All(result.Gaps, g => Assert.Equal(0, g.Present));
        Assert.Equal(result.Gaps.Count, result.CriticalCount + result.WarningCount);
        Assert.Contains(result.Gaps, g => g.Date == today.AddDays(7).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task NinetyDayWindow_WithinFourteenDays_IsNeverRecordKeeping()
    {
        // Direct regression for the rule the task restates: inside 14 days no open centre
        // short of MinRequired may be summarised away, whatever the requested window.
        // Here the far window has NO plan at all, so the location IS summarised — and the
        // point is that the next 14 days still come out as real gap rows, separately.
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "R1", ShiftDate = today.AddDays(120),
            ShiftType = ShiftTypes.Working, RawValue = "BO",
        });
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(90);

        Assert.Equal(today.AddDays(89).ToString("yyyy-MM-dd"), result.To);
        foreach (var expected in new[] { today, today.AddDays(7) })
        {
            Assert.Contains(result.Gaps, g => g.Date == expected.ToString("yyyy-MM-dd"));
        }
        // Record-keeping is reported SEPARATELY from the real gaps: the far days beyond
        // the 14-day window collapse into one summary row, while every gap row belongs to
        // the next 14 days — that separation is the rule.
        var summary = Assert.Single(result.NotRecorded);
        Assert.Equal("NOT_RECORDED", summary.Reason);
        Assert.True(summary.OpenDaysInWindow >= 10, "the far window is the rest of the 90 days");
        Assert.All(result.Gaps, g => Assert.True(
            DateOnly.Parse(g.Date) <= today.AddDays(13),
            $"{g.Date} is record-keeping territory but was listed as a real gap"));
    }
}
