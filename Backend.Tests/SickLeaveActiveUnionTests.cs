using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.SickLeave;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GSDDashboard.Tests;

// The Sick Leave page used to read the SickLeaves table alone. RTMs record ongoing sickness
// with the manual /shifts cell (an SL ShiftEntries row, deliberately no Krankmeldung row), so
// on 2026-10-13 the page said 0 currently sick while the Dashboard ("onSL": 13), the Overview
// absence feed (SL: 13) and /shifts (13 rows) all said 13.
//
// "Currently sick" is now the same union the rest of Pulse uses
// (AvailabilityResolver.GetAbsentIdsAsync: SickLeaves covering the date OR an SL ShiftEntry),
// and Child is counted like any other sick leave instead of being filtered out.
public class SickLeaveActiveUnionTests
{
    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private static void SeedEmployee(GSDContext db, string id, bool active = true) =>
        db.Employees.Add(new Employee { EmployeeId = id, FullName = "Agent " + id, IsActive = active, SourceSheet = "DE" });

    private static void SeedRecord(GSDContext db, string empId, string type, DateOnly from, DateOnly to) =>
        db.SickLeaves.Add(new SickLeave
        {
            EmployeeId = empId, FirstName = "Agent", LastName = empId,
            TeamLeadName = "TL-" + empId, FirstDay = from, LastDay = to,
            DurationDays = to.DayNumber - from.DayNumber + 1, LeaveType = type, SourceSheet = "UI",
        });

    private static void SeedSlShift(GSDContext db, string empId, DateOnly date) =>
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = empId, ShiftDate = date, ShiftType = "SL",
            SourceModule = "PATTERN_FILL", ManualChangeBy = "S60028",
            ManualChangeNote = "Manual shift cell edit (/shifts); automatic writers must preserve this row.",
        });

    private static SickLeaveService NewSvc(GSDContext db) =>
        new(db, new ShiftSyncService(db), NullLogger<SickLeaveService>.Instance);

    [Fact]
    public async Task ActiveSick_IsTheUnionOfRecordsAndSlShiftCells()
    {
        using var db = NewDb();
        foreach (var id in new[] { "E1", "E2", "E3", "E4", "E5", "E6" }) SeedEmployee(db, id, active: id != "E3");
        SeedRecord(db, "E1", "Self",  Today.AddDays(-3), Today.AddDays(2));
        SeedRecord(db, "E4", "Child", Today.AddDays(-1), Today.AddDays(5));
        SeedRecord(db, "E5", "AL",    Today.AddDays(-2), Today.AddDays(2)); // annual leave is not sickness
        SeedRecord(db, "E6", "Self",  Today,             Today.AddDays(1));
        SeedSlShift(db, "E2", Today);            // shift-cell only
        SeedSlShift(db, "E3", Today);            // inactive employee: never reported
        SeedSlShift(db, "E6", Today);            // already covered by a record: deduplicated
        await db.SaveChangesAsync();

        var svc = NewSvc(db);
        var active = await svc.GetActiveOnDateAsync(Today);

        Assert.Equal(new[] { "E1", "E2", "E4", "E6" }, active.Select(r => r.EmployeeId!).OrderBy(x => x).ToArray());

        // The record wins for an employee that has both; only the shift-only row is flagged.
        var e2 = active.Single(r => r.EmployeeId == "E2");
        Assert.True(e2.FromShift);
        Assert.Equal(0, e2.Id);
        Assert.Equal("Self", e2.LeaveType);
        Assert.Equal(Today.ToString("yyyy-MM-dd"), e2.FirstDay);
        Assert.All(active.Where(r => r.EmployeeId != "E2"), r => Assert.False(r.FromShift));
        Assert.DoesNotContain(active, r => r.EmployeeId == "E3");
        Assert.DoesNotContain(active, r => r.EmployeeId == "E5");
    }

    [Fact]
    public async Task Stats_CountChildAndAreConsistentWithTheActiveList()
    {
        using var db = NewDb();
        foreach (var id in new[] { "E1", "E2", "E4" }) SeedEmployee(db, id);
        SeedRecord(db, "E1", "Self",  Today.AddDays(-3), Today.AddDays(2));
        SeedRecord(db, "E4", "Child", Today.AddDays(-1), Today.AddDays(5));
        SeedSlShift(db, "E2", Today);
        await db.SaveChangesAsync();

        var stats = await NewSvc(db).GetStatsAsync(null, null);

        Assert.Equal(3, stats.TotalActive);
        Assert.Equal(2, stats.SelfCount);   // E1 (Self record) + E2 (shift-cell SL)
        Assert.Equal(1, stats.ChildCount);  // E4, previously always 0
        Assert.Equal(stats.TotalActive, stats.SelfCount + stats.ChildCount);
    }

    [Fact]
    public async Task ActiveOnlyList_MatchesTheCards_AndTheTypeFilterStillWorks()
    {
        using var db = NewDb();
        foreach (var id in new[] { "E1", "E2", "E4" }) SeedEmployee(db, id);
        SeedRecord(db, "E1", "Self",  Today.AddDays(-3), Today.AddDays(2));
        SeedRecord(db, "E4", "Child", Today.AddDays(-1), Today.AddDays(5));
        SeedSlShift(db, "E2", Today);
        await db.SaveChangesAsync();

        var svc = NewSvc(db);
        Assert.Equal(3, (await svc.GetSickLeavesAsync(null, null, null, null, true)).Count);
        Assert.Equal(new[] { "E4" }, (await svc.GetSickLeavesAsync(null, null, null, "Child", true))
            .Select(r => r.EmployeeId!).ToArray());
    }

    [Fact]
    public async Task ActiveOnlyList_IgnoresRecordTypesThatAreNotSickness()
    {
        using var db = NewDb();
        SeedEmployee(db, "E7");
        SeedRecord(db, "E7", "AL", Today.AddDays(-1), Today.AddDays(1));
        await db.SaveChangesAsync();

        Assert.Empty(await NewSvc(db).GetSickLeavesAsync(null, null, null, null, true));
    }

    // ── Overview "Absent today" ──────────────────────────────────────────────
    // RESIGNED keeps its place in FullAbsenceTypes (coverage: a resigned agent contributes 0)
    // but must never be reported as a person absent today.

    [Fact]
    public void AbsenceFeedTypes_ExcludeResigned_AndIncludeHalfDayAndLeave()
    {
        Assert.DoesNotContain("RESIGNED", AvailabilityResolver.AbsenceFeedTypes);
        foreach (var t in new[] { "SL", "AL", "UL", "OL", "PH", "LPH", "HALF_AL" })
            Assert.Contains(t, AvailabilityResolver.AbsenceFeedTypes);
        Assert.Contains("RESIGNED", AvailabilityResolver.FullAbsenceTypes); // coverage set unchanged
        Assert.DoesNotContain("OFF", AvailabilityResolver.AbsenceFeedTypes);
    }

    [Fact]
    public async Task Briefing_AbsentToday_ExcludesResigned_AndCountsHalfAl()
    {
        using var db = NewDb();
        foreach (var id in new[] { "E10", "E11", "E12", "E13", "E14" }) SeedEmployee(db, id);
        SeedSlShift(db, "E10", Today);
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "E11", ShiftDate = Today, ShiftType = "AL" });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "E12", ShiftDate = Today, ShiftType = "RESIGNED" });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "E13", ShiftDate = Today, ShiftType = "HALF_AL" });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "E14", ShiftDate = Today, ShiftType = "OFF" });
        await db.SaveChangesAsync();

        var subs = new SubstitutionService(db, new ReachabilityService(new UnusedScopeFactory()),
            new CoverageEvaluator(db), NullLogger<SubstitutionService>.Instance);
        var briefing = await new BriefingService(db, subs).GetBriefingAsync(Today.ToString("yyyy-MM-dd"));

        Assert.Equal(3, briefing.TotalAbsences); // SL + AL + HALF_AL
        Assert.DoesNotContain(briefing.Absences, a => a.ShiftType == "RESIGNED");
        Assert.DoesNotContain(briefing.Absences, a => a.EmployeeId == "E12");
        Assert.DoesNotContain(briefing.Absences, a => a.EmployeeId == "E14"); // OFF is not an absence
        Assert.Contains(briefing.Absences, a => a.EmployeeId == "E13");       // half a day is
    }

    private sealed class UnusedScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new InvalidOperationException("Reachability is not used by this test.");
    }
}
