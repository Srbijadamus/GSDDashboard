using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GSDDashboard.Tests;

// Regression suite for the five-surface coverage disagreement reported 2026-10-01:
// the Overview banner, the Coverage Risk strip and the KPI cards all said "no gaps /
// 100% covered", while "Gaps & Recommendations" listed 19 open centres as 0/1 UNCOVERED
// with a Find Substitute button and the WIC Locations page showed the same 19 as
// UNCOVERED. Every one of those 19 had a WIC_DUTY ShiftEntries row and no on-site
// WicShiftEntries row (RosterBatches 153-192, CreatedByKid=SYSTEM_WICFIX) — the
// documented 521-row import defect. BriefingService and WicCardsService were the two
// surfaces that read on-site rows ONLY, with no second signal.
//
// What is pinned here:
//   - a ROSTER_ONLY centre is staffed: out of the gap list, no substitute offered,
//     COVERED on the cards, and badged "check-in not recorded";
//   - a genuine NONE centre (open, present < min) IS still a gap — the whole point of
//     the feature must survive the fix;
//   - a once-weekly centre is not NOT_RECORDED at days=14 (the window-arithmetic bug);
//   - a centre whose opening hours live under LocationCodeLegacy resolves identically
//     in Briefing, Cards and Forecast (change E).
public class WicPresenceUnificationTests
{
    private const string Code = "DE~99999~Testheim~Teststr. 1";
    private const string Name = "Testheim";

    private static DbContextOptions<GSDContext> NewOptions() =>
        new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static GSDContext NewDb() => new(NewOptions());

    // Real DI graph over an in-memory DB, so BriefingService can be exercised end to end
    // (it depends on SubstitutionService -> ReachabilityService -> IServiceScopeFactory).
    private static ServiceProvider NewProvider(string dbName)
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));
        sc.AddDbContext<GSDContext>(o => o.UseInMemoryDatabase(dbName));
        sc.AddScoped<ReachabilityService>();
        sc.AddScoped<CoverageEvaluator>();
        sc.AddScoped<AvailabilityResolver>();
        sc.AddScoped<SubstitutionService>();
        sc.AddScoped<BriefingService>();
        sc.AddScoped<ForecastService>();
        sc.AddScoped<WicCardsService>();
        return sc.BuildServiceProvider();
    }

    private static WicLocation Loc(string? legacy = null) => new()
    {
        Id = 1, LocationCode = Code, LocationCodeLegacy = legacy,
        DisplayName = Name, City = "Testheim", MinAgentsRequired = 1, IsActive = true,
    };

    /// <summary>Seeds one centre open on <paramref name="date"/>'s weekday, 08:00-17:00.</summary>
    private static void SeedCentre(GSDContext db, DateOnly date, string? hoursUnderCode = null, string? legacy = null)
    {
        db.WicLocations.Add(Loc(legacy));
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = hoursUnderCode ?? Code,
            DayOfWeek = (int)date.DayOfWeek,
            OpenTime = "08:00", CloseTime = "17:00",
        });
        db.WicAgentAssignments.Add(new WicAgentAssignment
        {
            LocationCode = Code, EmployeeName = "Test Main", AssignmentType = "MAIN", IsActive = true,
        });
        db.Employees.Add(new Employee { EmployeeId = "T1", FullName = "Test Main", IsActive = true });
    }

    private static ShiftEntry WicDuty(DateOnly date) => new()
    {
        EmployeeId = "T1", ShiftDate = date, ShiftType = ShiftTypes.WicDuty,
        AgentTask = Name, ShiftStart = "08:00", ShiftEnd = "17:00", IsWicDuty = true,
    };

    // ── WicPresenceResolver: the one shared reading ──────────────────────────────

    [Fact]
    public void Resolver_ShiftEntryDutyWithNoOnSiteRow_IsRosterOnly_AndCounts()
    {
        var date = new DateOnly(2026, 10, 1);
        var duty = WicDuty(date);

        var presence = WicPresenceResolver.Resolve(
            Loc(), date,
            onSiteRows: new List<WicShiftEntry>(),
            shiftByEmpDate: new Dictionary<(string, DateOnly), ShiftEntry> { [("T1", date)] = duty },
            candidateDutyRows: new List<ShiftEntry> { duty },
            isSick: _ => false);

        Assert.Equal(WicCoverageEvidence.ROSTER_ONLY, presence.Evidence);
        Assert.Equal(1, presence.EffectiveCoverage);
        Assert.Equal(1, presence.CheckInNotRecordedCount);
        Assert.Equal(0, presence.OnSiteCount);
    }

    [Fact]
    public void Resolver_AgentOnBothSignals_IsCountedOnce_AndReportsOnSite()
    {
        var date = new DateOnly(2026, 10, 1);
        var duty = WicDuty(date);
        var onSite = new WicShiftEntry
        {
            EmployeeId = "T1", ShiftDate = date, SupportLocation = Name, IsOnSite = true, Task = "WIC",
        };

        var presence = WicPresenceResolver.Resolve(
            Loc(), date,
            onSiteRows: new List<WicShiftEntry> { onSite },
            shiftByEmpDate: new Dictionary<(string, DateOnly), ShiftEntry> { [("T1", date)] = duty },
            candidateDutyRows: new List<ShiftEntry> { duty },
            isSick: _ => false);

        Assert.Equal(WicCoverageEvidence.ON_SITE, presence.Evidence);
        Assert.Equal(1, presence.EffectiveCoverage);       // not 2 — deduplicated by employee
        Assert.Equal(0, presence.CheckInNotRecordedCount);
    }

    [Fact]
    public void Resolver_NobodyRostered_IsNone()
    {
        var date = new DateOnly(2026, 10, 1);
        var presence = WicPresenceResolver.Resolve(
            Loc(), date,
            onSiteRows: new List<WicShiftEntry>(),
            shiftByEmpDate: new Dictionary<(string, DateOnly), ShiftEntry>(),
            candidateDutyRows: new List<ShiftEntry>(),
            isSick: _ => false);

        Assert.Equal(WicCoverageEvidence.NONE, presence.Evidence);
        Assert.Equal(0, presence.EffectiveCoverage);
    }

    [Fact]
    public void Resolver_AbsentAgentWithStaleCentreTask_IsNone_NotRosterOnly()
    {
        // Must not "staff" a centre from an AL row that merely kept its old AgentTask.
        var date = new DateOnly(2026, 10, 1);
        var al = new ShiftEntry
        {
            EmployeeId = "T1", ShiftDate = date, ShiftType = ShiftTypes.AnnualLeave, AgentTask = Name,
        };

        var presence = WicPresenceResolver.Resolve(
            Loc(), date,
            onSiteRows: new List<WicShiftEntry>(),
            shiftByEmpDate: new Dictionary<(string, DateOnly), ShiftEntry> { [("T1", date)] = al },
            candidateDutyRows: new List<ShiftEntry> { al },
            isSick: _ => false);

        Assert.Equal(WicCoverageEvidence.NONE, presence.Evidence);
        Assert.Equal(0, presence.CheckInNotRecordedCount);
    }

    // ── Gaps & Recommendations (BriefingService) ─────────────────────────────────

    [Fact]
    public async Task Briefing_RosterOnlyCentre_IsNotAGap_AndOffersNoSubstitute()
    {
        var date = new DateOnly(2026, 10, 1);
        var dbName = Guid.NewGuid().ToString();
        await using var sp = NewProvider(dbName);

        using (var seed = sp.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<GSDContext>();
            SeedCentre(db, date);
            db.ShiftEntries.Add(WicDuty(date));   // rostered, but no on-site row at all
            await db.SaveChangesAsync();
        }

        using var scope = sp.CreateScope();
        var briefing = await scope.ServiceProvider.GetRequiredService<BriefingService>()
            .GetBriefingAsync(date.ToString("yyyy-MM-dd"));

        Assert.Empty(briefing.Gaps);          // no row => no Find Substitute button
        Assert.Equal(0, briefing.TotalGaps);
    }

    [Fact]
    public async Task Briefing_GenuinelyUnstaffedOpenCentre_IsStillAGap()
    {
        // The feature has to keep working: open day, MAIN agent doing non-WIC work,
        // nobody on site => present 0 < min 1 => a real gap.
        var date = new DateOnly(2026, 10, 1);
        var dbName = Guid.NewGuid().ToString();
        await using var sp = NewProvider(dbName);

        using (var seed = sp.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<GSDContext>();
            SeedCentre(db, date);
            db.ShiftEntries.Add(new ShiftEntry
            {
                EmployeeId = "T1", ShiftDate = date, ShiftType = ShiftTypes.Working,
                RawValue = "BO", ShiftStart = "08:00", ShiftEnd = "17:00",
            });
            await db.SaveChangesAsync();
        }

        using var scope = sp.CreateScope();
        var briefing = await scope.ServiceProvider.GetRequiredService<BriefingService>()
            .GetBriefingAsync(date.ToString("yyyy-MM-dd"));

        var gap = Assert.Single(briefing.Gaps);
        Assert.Equal(Name, gap.DisplayName);
        Assert.Equal(0, gap.Present);
        Assert.Equal(1, gap.Required);
        Assert.Equal("UNCOVERED", gap.Status);
    }

    // ── WIC Locations page (WicCardsService) ─────────────────────────────────────

    [Fact]
    public async Task Cards_RosterOnlyCentre_IsCovered_BadgedAndAgentListed()
    {
        var date = new DateOnly(2026, 10, 1);
        using var db = NewDb();
        SeedCentre(db, date);
        db.ShiftEntries.Add(WicDuty(date));
        await db.SaveChangesAsync();

        var card = Assert.Single(await new WicCardsService(db).GetCardsAsync(date, null));

        Assert.Equal("COVERED", card.CoverageStatus);              // was UNCOVERED
        Assert.Equal("ROSTER_ONLY", card.CoverageEvidence);
        Assert.Equal(1, card.CheckInNotRecordedCount);             // drives the card badge
        var agent = Assert.Single(card.AssignedAgents);            // card is no longer empty
        Assert.Equal("Test Main", agent.Name);
        Assert.True(agent.CheckInNotRecorded);
        Assert.Equal("FULL", agent.CoverageMatch);
    }

    [Fact]
    public async Task Cards_GenuinelyUnstaffedOpenCentre_IsStillUncovered()
    {
        var date = new DateOnly(2026, 10, 1);
        using var db = NewDb();
        SeedCentre(db, date);
        await db.SaveChangesAsync();

        var card = Assert.Single(await new WicCardsService(db).GetCardsAsync(date, null));

        Assert.Equal("UNCOVERED", card.CoverageStatus);
        Assert.Equal("NONE", card.CoverageEvidence);
        Assert.Equal(0, card.CheckInNotRecordedCount);             // no badge on a real gap
    }

    // ── Banner (WicCoverageGapService) — change D ────────────────────────────────

    [Fact]
    public async Task Banner_OnceWeeklyCentre_IsNotNotRecorded_AtDays14()
    {
        // Built relative to today so it is date-agnostic: a centre open on exactly one
        // weekday has its only open days in a 14-day window at day 0 and day 7, and the
        // NOT_RECORDED cutoff is "> day 7" — so every open day fell inside the cutoff and
        // the centre was reported NOT_RECORDED despite duty on every one of them.
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        db.ShiftEntries.Add(WicDuty(today));
        db.ShiftEntries.Add(WicDuty(today.AddDays(7)));
        // Push the roster horizon past the window so `to` is not clamped back.
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "T1", ShiftDate = today.AddDays(20),
            ShiftType = ShiftTypes.Working, RawValue = "BO",
        });
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(14);

        Assert.Empty(result.NotRecorded);   // was one NOT_RECORDED row
        Assert.Empty(result.Gaps);
        Assert.Equal(0, result.CriticalCount);
        Assert.Equal(0, result.WarningCount);
    }

    [Fact]
    public async Task Banner_CentreWithNoDutyRecordedInWindow_IsAGapNotRecordKeeping()
    {
        // Updated 2026-10-13 (rule 2): a centre that is OPEN inside the next 14 days and
        // has fewer agents than MinRequired is a day-by-day GAP. The record-keeping bucket
        // may only describe open days beyond that horizon — it may never silence an
        // actionable gap and leave the banner green (the Demmin/Wesel case).
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "T1", ShiftDate = today.AddDays(20),
            ShiftType = ShiftTypes.Working, RawValue = "BO",
        });
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(14);

        Assert.Empty(result.NotRecorded);
        Assert.Equal(2, result.Gaps.Count);   // this weekday, today and today+7
        Assert.All(result.Gaps, g => Assert.Equal(0, g.Present));
    }

    // ── Change E: hours stored under LocationCodeLegacy ──────────────────────────

    [Fact]
    public async Task LegacyCodeHours_ResolveIdenticallyInForecastCardsAndBriefing()
    {
        // Hours exist ONLY under the legacy bridge code. Forecast always passed the legacy
        // code; Briefing and Cards did not, so they read "no hours row" => closed, and the
        // centre silently dropped out of both while Forecast showed it open.
        var date = new DateOnly(2026, 10, 1);
        const string legacy = "DE_Legacy_Testheim";
        var dbName = Guid.NewGuid().ToString();
        await using var sp = NewProvider(dbName);

        using (var seed = sp.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<GSDContext>();
            SeedCentre(db, date, hoursUnderCode: legacy, legacy: legacy);
            await db.SaveChangesAsync();   // deliberately nobody rostered
        }

        using var scope = sp.CreateScope();
        var p = scope.ServiceProvider;

        var forecast = await p.GetRequiredService<ForecastService>().GetForecastAsync(1, Code, date);
        var day = Assert.Single(Assert.Single(forecast.Locations).Forecast);
        Assert.True(day.IsOpen);
        Assert.Equal("UNCOVERED", day.Status);

        var card = Assert.Single(await p.GetRequiredService<WicCardsService>().GetCardsAsync(date, null));
        Assert.False(card.TodaySchedule.IsClosed);        // was CLOSED
        Assert.Equal("08:00", card.TodaySchedule.OpenTime);
        Assert.Equal("UNCOVERED", card.CoverageStatus);

        var briefing = await p.GetRequiredService<BriefingService>()
            .GetBriefingAsync(date.ToString("yyyy-MM-dd"));
        var gap = Assert.Single(briefing.Gaps);           // was skipped as closed
        Assert.Equal(Name, gap.DisplayName);
    }
}
