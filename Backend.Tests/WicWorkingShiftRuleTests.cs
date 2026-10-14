using System.Net.Http.Json;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Rollout;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.SubstituteAccept;
using GSDDashboard.API.Modules.Vwic;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GSDDashboard.Tests;

// RTM rule 2026-10-14 (point 6 + point 7):
//
//   WicShiftEntries.WorkingShift for an on-site row = INTERSECTION of the agent's own
//   ShiftEntries window and the centre's resolved window (WicHoursResolver + DutyWindow).
//   The agent's ShiftEntries times are never changed because of this rule.
//
// These tests pin the rule for the shared helper, for every writer that used to write
// centre hours / request times verbatim (Assign Agent, + New Shift, Shift Plan sync,
// Substitute accept, Rollout, VWIC) and for the conflict detector, which now compares
// the trimmed windows. The scenario from the brief: agent shift 07:30-16:30,
// centre A 10:00-12:00 + centre B 13:00-15:00 -> no conflict; centre A 10:00-14:00 +
// centre B 13:00-15:00 -> conflict.
public class WicWorkingShiftRuleTests
{
    private const string CodeA = "DE~17109~Demmin~Am Hanseufer 2";
    private const string NameA = "Demmin - Am Hanseufer";
    private const string CodeB = "DE~17109~Demmin~Woldeforster Str. 6";
    private const string NameB = "Demmin - Woldeforster Str";

    private static readonly DateOnly Thu = new(2026, 10, 8); // 2026-10-08 is a Thursday

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static WicOpeningHour Hours(string code, string open, string close) =>
        new() { LocationCode = code, DayOfWeek = (int)Thu.DayOfWeek, OpenTime = open, CloseTime = close };

    // ── The shared helper ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("07:30", "16:30", "10:00", "14:00", "10:00-14:00")] // agent wider than centre
    [InlineData("08:00", "12:00", "10:00", "14:00", "10:00-12:00")] // centre wider than agent
    [InlineData("07:30", "16:30", "10:00", "12:00", "10:00-12:00")]
    public void Slice_ReturnsTheIntersection(string aS, string aE, string cS, string cE, string expected) =>
        Assert.Equal(expected, WicWorkingShift.Slice(aS, aE, cS, cE));

    [Fact]
    public void Slice_WithNoCentreWindow_KeepsTheAgentsWindow() =>
        Assert.Equal("07:30-16:30", WicWorkingShift.Slice("07:30", "16:30", null, null));

    [Fact]
    public void Slice_WithNoAgentWindow_UsesTheCentreWindow() =>
        Assert.Equal("10:00-14:00", WicWorkingShift.Slice(null, null, "10:00", "14:00"));

    [Fact]
    public void Slice_DisjointWindows_IsNoIntersection() =>
        Assert.Null(WicWorkingShift.Slice("07:30", "09:00", "10:00", "14:00"));

    [Fact]
    public void Parse_TwoBlockValue_SpansFirstStartToLastEnd()
    {
        var w = WicWorkingShift.Parse("07:00-12:00 / 12:30-13:30");
        Assert.NotNull(w);
        Assert.Equal("07:00", w!.Value.Start.ToString("HH:mm"));
        Assert.Equal("13:30", w.Value.End.ToString("HH:mm"));
    }

    [Fact]
    public void EffectiveWindow_CutsTheStoredShiftToTheCentreWindow()
    {
        var (s, e) = WicWorkingShift.EffectiveWindow("07:30-16:30", Hours(CodeA, "10:00", "12:00"));
        Assert.Equal("10:00", s);
        Assert.Equal("12:00", e);
    }

    [Fact]
    public void EffectiveWindow_StoredShiftOutsideTheCentreWindow_IsNoWindow()
    {
        var (s, e) = WicWorkingShift.EffectiveWindow("07:30-09:00", Hours(CodeA, "10:00", "12:00"));
        Assert.Null(s);
        Assert.Null(e);
    }

    // ── Point 7 / point 8: the conflict detector on trimmed times ────────────────

    private static void SeedTwoCentres(GSDContext db, string openA, string closeA, string openB, string closeB, string agentShift)
    {
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Trim Agent", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = CodeA, DisplayName = NameA, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = CodeB, DisplayName = NameB, IsActive = true });
        db.WicOpeningHours.Add(Hours(CodeA, openA, closeA));
        db.WicOpeningHours.Add(Hours(CodeB, openB, closeB));
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Thu, DayOfWeek = Thu.DayOfWeek.ToString(),
            SupportLocation = NameA, WorkingShift = agentShift, IsOnSite = true, Task = "WIC",
        });
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Thu, DayOfWeek = Thu.DayOfWeek.ToString(),
            SupportLocation = NameB, WorkingShift = agentShift, IsOnSite = true, Task = "WIC",
        });
    }

    [Fact]
    public async Task ConflictDetector_TrimmedWindowsDoNotTouch_NoConflict()
    {
        using var db = NewDb();
        SeedTwoCentres(db, "10:00", "12:00", "13:00", "15:00", agentShift: "07:30-16:30");
        await db.SaveChangesAsync();

        var detector = new WicConflictDetector(db);
        Assert.Empty(await detector.GetConflictsAsync(Thu, Thu)); // no OVERLAP

        // The day is still a (non-overlapping) split shift when asked for it.
        var detailed = Assert.Single(await detector.GetConflictsAsync(Thu, Thu, includeSplitShifts: true));
        Assert.Equal("SPLIT_SHIFT", detailed.ConflictType);
    }

    [Fact]
    public async Task ConflictDetector_TrimmedWindowsOverlap_IsConflict()
    {
        using var db = NewDb();
        SeedTwoCentres(db, "10:00", "14:00", "13:00", "15:00", agentShift: "07:30-16:30");
        await db.SaveChangesAsync();

        var conflict = Assert.Single(await new WicConflictDetector(db).GetConflictsAsync(Thu, Thu));
        Assert.Equal("OVERLAP", conflict.ConflictType);
        Assert.Contains(conflict.Locations, l => l.OpenTime == "10:00" && l.CloseTime == "14:00");
        Assert.Contains(conflict.Locations, l => l.OpenTime == "13:00" && l.CloseTime == "15:00");
    }

    // ── Point 6: every writer applies the rule ───────────────────────────────────

    private static void SeedAgent(GSDContext db, string? window = "07:30-16:30")
    {
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Rule Agent", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = CodeA, DisplayName = NameA, IsActive = true });
        db.WicOpeningHours.Add(Hours(CodeA, "10:00", "12:00"));
        if (window != null)
            db.ShiftEntries.Add(new ShiftEntry
            {
                EmployeeId = "E1", ShiftDate = Thu, ShiftType = ShiftTypes.Working,
                ShiftStart = window[..5], ShiftEnd = window[6..],
            });
    }

    // Assign Agent (POST /api/wic/assignments -> WicShiftService.CreateAssignmentAsync):
    // an explicit request window wider than the centre is cut to the centre; the agent's
    // own ShiftEntries row keeps the requested window.
    [Fact]
    public async Task CreateAssignment_RequestWiderThanCentre_WicRowIsCut_AgentShiftKept()
    {
        using var db = NewDb();
        SeedAgent(db, window: null);
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", CodeA, Thu.ToString("yyyy-MM-dd"), "07:30", "16:30"), db);

        Assert.Equal("10:00-12:00", (await db.WicShiftEntries.SingleAsync()).WorkingShift);
        var shift = await db.ShiftEntries.SingleAsync();
        Assert.Equal("07:30", shift.ShiftStart);
        Assert.Equal("16:30", shift.ShiftEnd);
    }

    // + New Shift (WicShiftService.CreateShiftAsync): a null-time edit keeps the agent's
    // window and the WIC row is cut to the centre — the centre's hours never become the
    // agent's shift.
    [Fact]
    public async Task CreateShift_NullTimeEdit_KeepsAgentWindow_AndCutsTheWicRow()
    {
        using var db = NewDb();
        SeedAgent(db);
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        await svc.CreateShiftAsync(
            new CreateShiftRequest("E1", Thu.ToString("yyyy-MM-dd"), ShiftTypes.WicDuty, null, null, null, CodeA),
            db, new ShiftValidationService(db), "TEST-OPERATOR");

        Assert.Equal("10:00-12:00", (await db.WicShiftEntries.SingleAsync()).WorkingShift);
        var shift = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E1");
        Assert.Equal(ShiftTypes.WicDuty, shift.ShiftType);
        Assert.Equal("07:30", shift.ShiftStart);
        Assert.Equal("16:30", shift.ShiftEnd);
    }

    // Shift Plan sync (ShiftService.SyncWicDutyFromShiftPlanAsync via UpdateShiftAsync):
    // a cell wider than the centre is cut on the WIC row; the cell keeps its own times.
    [Fact]
    public async Task ShiftPlanSync_WiderCell_WicRowIsCutToTheCentreWindow()
    {
        using var db = NewDb();
        SeedAgent(db, window: null);
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 501, EmployeeId = "E1", ShiftDate = Thu, ShiftType = ShiftTypes.Working,
            ShiftStart = "07:30", ShiftEnd = "16:30",
        });
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.UpdateShiftAsync(501,
            new ShiftUpdateDto(ShiftTypes.WicDuty, "07:30", "16:30", null, CodeA, null), "TEST-OPERATOR");

        Assert.Null(result.Error);
        Assert.Equal("10:00-12:00", (await db.WicShiftEntries.SingleAsync()).WorkingShift);
        var cell = await db.ShiftEntries.SingleAsync(s => s.Id == 501);
        Assert.Equal("07:30", cell.ShiftStart);
        Assert.Equal("16:30", cell.ShiftEnd);
    }

    // Substitute accept (POST /api/wic/substitutes/accept): the WIC row is the agent's
    // window cut to the centre's window, not the centre's raw hours.
    [Fact]
    public async Task SubstituteAccept_WritesTheIntersection()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<GSDContext>(o => o.UseInMemoryDatabase(database));
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapSubstituteAcceptEndpoints();
        using (var scope = app.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<GSDContext>();
            SeedAgent(seedDb);
            await seedDb.SaveChangesAsync();
        }
        await app.StartAsync();

        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
            { BaseAddress = new Uri(address) };
            using var res = await client.PostAsJsonAsync("/api/wic/substitutes/accept", new
            {
                employeeId = "E1",
                locationCode = CodeA,
                date = Thu.ToString("yyyy-MM-dd"),
                shiftStart = (string?)null,
                shiftEnd = (string?)null,
                sourceType = "BACKUP",
            });
            Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);

            using var scope = app.Services.CreateScope();
            var dbOut = scope.ServiceProvider.GetRequiredService<GSDContext>();
            Assert.Equal("10:00-12:00", (await dbOut.WicShiftEntries.SingleAsync()).WorkingShift);
            var shift = await dbOut.ShiftEntries.SingleAsync();
            Assert.Equal("07:30", shift.ShiftStart);
            Assert.Equal("16:30", shift.ShiftEnd);
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }

    // Rollout: the rollout day's hours are the centre's hours for that date; the worker's
    // own shift (10:00-12:00) is the narrower factor, so the WIC row is the intersection
    // and the agent's ShiftEntries row is kept untouched.
    [Fact]
    public async Task Rollout_WritesTheIntersectionWithTheWorkersShift()
    {
        using var db = NewDb();
        SeedAgent(db, window: "10:00-12:00");
        await db.SaveChangesAsync();

        var svc = new RolloutService(db);
        var result = await svc.CreateAsync(new SaveRolloutRequest(
            CodeA, Thu.ToString("yyyy-MM-dd"), "08:00", "14:00", "Onboarding",
            new List<RolloutWorkerRequest> { new("E1", false, null) }), "TEST-OPERATOR");

        Assert.True(result.Success, result.Reason);
        var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E1" && w.IsOnSite);
        Assert.Equal("10:00-12:00", wic.WorkingShift);   // 10:00-12:00 ∩ 08:00-14:00
        var shift = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E1");
        Assert.Equal("10:00", shift.ShiftStart);
        Assert.Equal("12:00", shift.ShiftEnd);
    }

    // VWIC is a pseudo-centre with no opening hours: the rule's centre factor is empty, so
    // the working shift is the agent's own window (not the 07:00/18:00 default).
    [Fact]
    public async Task Vwic_UsesTheAgentsOwnWindow_BecauseThereIsNoCentreWindow()
    {
        using var db = NewDb();
        SeedAgent(db);
        await db.SaveChangesAsync();

        var svc = new VwicService(db);
        await svc.AssignAgentAsync(new VwicAssignRequest("E1", Thu.ToString("yyyy-MM-dd"), 8, null, null));

        Assert.Equal("07:30 - 16:30", (await db.WicShiftEntries.SingleAsync()).WorkingShift);
    }
}
