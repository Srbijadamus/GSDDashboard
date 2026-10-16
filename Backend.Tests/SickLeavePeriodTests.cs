using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.SickLeave;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GSDDashboard.Tests;

// 2026-10-16: "Currently Sick Agents" and the Overview absence feed showed nearly every sick
// agent as "sick since <today>, expected return <tomorrow>, 1d" while RTM saw weeks-long
// sicknesses. The history was in the database (a chain of manual /shifts SL ShiftEntries rows,
// often continuing past the Krankmeldung that started the spell) — the readers only looked at
// a single row. These tests pin the ONE shared rule: consecutive sick days are one period when
// the days between them are not working days (weekend / public holiday / OFF), "sick since" is
// the first day of the period, "expected return" is the first working day after the last
// recorded sick day.
public class SickLeavePeriodTests
{
    // 2026-10-12 is a Monday, so Mon..Fri are working days and Mon+5/Mon+6 are the weekend.
    private static readonly DateOnly Mon = new(2026, 10, 12);
    private static DateOnly Fri => Mon.AddDays(4);
    private static DateOnly NextMon => Mon.AddDays(7);

    private static bool WeekendOnly(DateOnly d) => SickLeavePeriodResolver.IsWeekend(d);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static void SeedEmployee(GSDContext db, string id, bool active = true) =>
        db.Employees.Add(new Employee { EmployeeId = id, FullName = "Agent " + id, IsActive = active, SourceSheet = "DE" });

    private static void SeedSlShift(GSDContext db, string empId, DateOnly date) =>
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = empId, ShiftDate = date, ShiftType = "SL",
            SourceModule = "PATTERN_FILL", ManualChangeBy = "S60028",
            ManualChangeNote = "Manual shift cell edit (/shifts); automatic writers must preserve this row.",
        });

    private static void SeedRecord(GSDContext db, string empId, string type, DateOnly from, DateOnly to) =>
        db.SickLeaves.Add(new SickLeave
        {
            EmployeeId = empId, FirstName = "Agent", LastName = empId,
            TeamLeadName = "TL-" + empId, FirstDay = from, LastDay = to,
            DurationDays = to.DayNumber - from.DayNumber + 1, LeaveType = type, SourceSheet = "UI",
        });

    private static SickLeaveService NewSvc(GSDContext db) =>
        new(db, new ShiftSyncService(db), NullLogger<SickLeaveService>.Instance);

    // ── Pure merge rule ──────────────────────────────────────────────────────

    [Fact]
    public void Merge_MondayToFriday_IsOnePeriod()
    {
        var raw = Enumerable.Range(0, 5).Select(i => new SickPeriod(Mon.AddDays(i), Mon.AddDays(i)));
        var merged = SickLeavePeriodResolver.Merge(raw, WeekendOnly);

        var period = Assert.Single(merged);
        Assert.Equal(Mon, period.FirstDay);
        Assert.Equal(Fri, period.LastDay);
        Assert.Equal(5, period.CalendarDays);
    }

    [Fact]
    public void Merge_FridayAndMonday_AcrossTheWeekend_IsOnePeriod()
    {
        var merged = SickLeavePeriodResolver.Merge(
            new[] { new SickPeriod(Fri, Fri), new SickPeriod(NextMon, NextMon) }, WeekendOnly);

        var period = Assert.Single(merged);
        Assert.Equal(Fri, period.FirstDay);
        Assert.Equal(NextMon, period.LastDay);
    }

    [Fact]
    public void Merge_OffDayBetweenTwoSickDays_IsOnePeriod()
    {
        var offDays = new HashSet<DateOnly> { Mon.AddDays(1) }; // Tuesday is an OFF day
        var merged = SickLeavePeriodResolver.Merge(
            new[] { new SickPeriod(Mon, Mon), new SickPeriod(Mon.AddDays(2), Mon.AddDays(2)) },
            d => WeekendOnly(d) || offDays.Contains(d));

        var period = Assert.Single(merged);
        Assert.Equal(Mon, period.FirstDay);
        Assert.Equal(Mon.AddDays(2), period.LastDay);
    }

    [Fact]
    public void Merge_WorkingDayBetweenTwoSickDays_StaysTwoPeriods()
    {
        var merged = SickLeavePeriodResolver.Merge(
            new[] { new SickPeriod(Mon, Mon), new SickPeriod(Mon.AddDays(2), Mon.AddDays(2)) }, WeekendOnly);

        Assert.Equal(2, merged.Count);
        Assert.Equal(Mon, merged[0].FirstDay);
        Assert.Equal(Mon.AddDays(2), merged[1].FirstDay);
    }

    [Fact]
    public void ExpectedReturn_SkipsTheWeekend()
    {
        var period = new SickPeriod(Mon, Fri);
        var ret = SickLeavePeriodResolver.ExpectedReturn(period, d => !WeekendOnly(d));
        Assert.Equal(NextMon, ret);
    }

    [Fact]
    public void ExpectedReturn_SkipsPublicHolidayAndOffDays()
    {
        var holidays = new HashSet<DateOnly> { NextMon };
        var offDays = new HashSet<DateOnly> { NextMon.AddDays(1) };
        var period = new SickPeriod(Mon, Fri);

        var ret = SickLeavePeriodResolver.ExpectedReturn(period,
            d => !WeekendOnly(d) && !holidays.Contains(d) && !offDays.Contains(d));

        Assert.Equal(NextMon.AddDays(2), ret); // Wednesday
    }

    [Fact]
    public void Merge_SpansTheLongestRecordAndShiftCellChain()
    {
        // Krankmeldung 05.–06., manual SL cells 07.–09. and 12.–16. (the weekend between them)
        // are ONE spell: 05. → 16.
        var start = new DateOnly(2026, 10, 5);
        var raw = new List<SickPeriod> { new(start, start.AddDays(1)) };
        foreach (var day in new[] { 7, 8, 9, 12, 13, 14, 15, 16 })
            raw.Add(new SickPeriod(start.AddDays(day - 5), start.AddDays(day - 5)));

        var merged = SickLeavePeriodResolver.Merge(raw, WeekendOnly);

        var period = Assert.Single(merged);
        Assert.Equal(start, period.FirstDay);
        Assert.Equal(new DateOnly(2026, 10, 16), period.LastDay);
    }

    // ── The shared rule through the services ────────────────────────────────

    [Fact]
    public async Task ActiveSick_ReportsTheFirstDayOfTheSpell_NotTodaysRow()
    {
        using var db = NewDb();
        SeedEmployee(db, "P1");
        // Two manual SL shift cells only (Thu + Fri), no Krankmeldung at all.
        SeedSlShift(db, "P1", new DateOnly(2026, 10, 15));
        SeedSlShift(db, "P1", new DateOnly(2026, 10, 16));
        await db.SaveChangesAsync();

        var row = Assert.Single(await NewSvc(db).GetActiveOnDateAsync(new DateOnly(2026, 10, 16)));

        Assert.Equal("P1", row.EmployeeId);
        Assert.Equal("2026-10-15", row.PeriodStart);   // NOT 2026-10-16
        Assert.Equal("2026-10-16", row.PeriodEnd);
        Assert.Equal(2, row.PeriodDays);
        Assert.Equal(2, row.DaysSoFar);
        Assert.Equal("2026-10-19", row.ExpectedReturn); // Monday, weekend skipped
    }

    [Fact]
    public async Task ActiveSick_JoinsTheKrankmeldungWithTheManualCellsThatContinueIt()
    {
        using var db = NewDb();
        SeedEmployee(db, "P2");
        // Krankmeldung ended 06.10., RTM continued the sickness day by day on /shifts up to 16.10.
        SeedRecord(db, "P2", "Self", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6));
        foreach (var day in new[] { 7, 8, 9, 12, 13, 14, 15, 16 })
            SeedSlShift(db, "P2", new DateOnly(2026, 10, day));
        await db.SaveChangesAsync();

        var row = Assert.Single(await NewSvc(db).GetActiveOnDateAsync(new DateOnly(2026, 10, 16)));

        Assert.Equal("2026-10-05", row.PeriodStart);
        Assert.Equal("2026-10-16", row.PeriodEnd);
        Assert.Equal(12, row.DaysSoFar);        // 05.10. → 16.10. inclusive
        Assert.Equal("2026-10-19", row.ExpectedReturn);
    }

    [Fact]
    public async Task ActiveSick_TwoSeparateSpells_ReportsTheOneAroundTheReferenceDay()
    {
        using var db = NewDb();
        SeedEmployee(db, "P3");
        SeedRecord(db, "P3", "Self", new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12)); // Monday
        // Tue + Wed are ordinary working days between the two spells, so they stay apart.
        foreach (var day in new[] { 15, 16 })
            SeedSlShift(db, "P3", new DateOnly(2026, 10, day));
        await db.SaveChangesAsync();

        var row = Assert.Single(await NewSvc(db).GetActiveOnDateAsync(new DateOnly(2026, 10, 16)));
        Assert.Equal("2026-10-15", row.PeriodStart);
        Assert.Equal("2026-10-16", row.PeriodEnd);
    }

    [Fact]
    public async Task Briefing_Absence_UsesTheMergedSpell()
    {
        using var db = NewDb();
        SeedEmployee(db, "P10");
        foreach (var day in new[] { 12, 13, 14, 15, 16 })
            SeedSlShift(db, "P10", new DateOnly(2026, 10, day));
        await db.SaveChangesAsync();

        var subs = new SubstitutionService(db, new ReachabilityService(new UnusedScopeFactory()),
            new CoverageEvaluator(db), NullLogger<SubstitutionService>.Instance);
        var briefing = await new BriefingService(db, subs).GetBriefingAsync("2026-10-16");

        var ab = Assert.Single(briefing.Absences, a => a.EmployeeId == "P10");
        Assert.Equal("2026-10-12", ab.FirstDay);
        Assert.Equal("2026-10-16", ab.LastDay);
        Assert.Equal(5, ab.TotalDays);
        Assert.Equal(5, ab.DaysSoFar);
    }

    private sealed class UnusedScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new InvalidOperationException("Reachability is not used by this test.");
    }
}
