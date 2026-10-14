using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// RTM fact 2026-10-15 (points 3, 4 and 5 of the multi-centre task):
//
//   An agent may work in TWO centres on the same day — 10:00-12:00 centre A, 13:00-15:00
//   centre B. WicShiftEntries keeps ONE ROW PER CENTRE, and the single daily ShiftEntries
//   row must cover EVERY one of his WIC parts (earliest start .. latest end), because
//   ShiftEntries is unique per employee+date.
//
// What these tests pin:
//   * the shared helpers (MultiCentreSpan / Widen / WidenDaily);
//   * point 3 — Assign Agent, + New Shift and the Shift Plan cell edit all widen the daily
//     shift to the span of the day's centres, and no single-centre edit may ever narrow it
//     so that the other centre falls out;
//   * point 4 — a WIC row whose centre does not touch the agent's window carries THAT
//     CENTRE's hours, never the day's span (otherwise both rows would claim the same time);
//   * point 5 — the exact brief scenario: 10-12 A + 13-15 B -> daily shift 10:00-15:00,
//     two WIC rows, and the conflict detector reports NO conflict.
public class WicMultiCentreDayTests
{
    private const string CodeA = "DE~17109~Demmin~Am Hanseufer 2";
    private const string NameA = "Demmin - Am Hanseufer";
    private const string CodeB = "DE~17109~Demmin~Woldeforster Str. 6";
    private const string NameB = "Demmin - Woldeforster Str";

    private static readonly DateOnly Thu = new(2026, 10, 8); // 2026-10-08 is a Thursday

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // A = 10:00-12:00, B = 13:00-15:00 on the same weekday — the Demmin pattern.
    private static void SeedTwoCentres(GSDContext db)
    {
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Multi Centre Agent", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = CodeA, DisplayName = NameA, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = CodeB, DisplayName = NameB, IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour
        { LocationCode = CodeA, DayOfWeek = (int)Thu.DayOfWeek, OpenTime = "10:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour
        { LocationCode = CodeB, DayOfWeek = (int)Thu.DayOfWeek, OpenTime = "13:00", CloseTime = "15:00" });
    }

    private static ShiftEntry DailyShift(GSDContext db, string start, string end, int id = 501) =>
        new()
        {
            Id = id, EmployeeId = "E1", ShiftDate = Thu, ShiftType = ShiftTypes.WicDuty,
            IsWicDuty = true, ShiftStart = start, ShiftEnd = end,
        };

    private static WicShiftEntry WicRow(string name, string workingShift) =>
        new()
        {
            EmployeeId = "E1", ShiftDate = Thu, DayOfWeek = Thu.DayOfWeek.ToString(),
            SupportLocation = name, WorkingShift = workingShift, IsOnSite = true, Task = "WIC",
        };

    // ── The shared helpers ───────────────────────────────────────────────────────

    [Fact]
    public void MultiCentreSpan_IsEarliestStartToLatestEnd()
    {
        var span = WicWorkingShift.MultiCentreSpan(new[] { "10:00-12:00", "13:00-15:00" });
        Assert.NotNull(span);
        Assert.Equal("10:00", span!.Value.Start.ToString("HH:mm"));
        Assert.Equal("15:00", span.Value.End.ToString("HH:mm"));
    }

    [Fact]
    public void MultiCentreSpan_SingleCentre_IsNotAMultiCentreDay() =>
        Assert.Null(WicWorkingShift.MultiCentreSpan(new[] { "10:00-12:00" }));

    [Fact]
    public void MultiCentreSpan_UnparseablePartsAreIgnored()
    {
        Assert.Null(WicWorkingShift.MultiCentreSpan(new[] { "10:00-12:00", null, "", "not a time" }));
    }

    [Fact]
    public void Widen_OnlyEverGrowsTheWindow()
    {
        // narrower row is widened
        var grown = WicWorkingShift.Widen((new TimeOnly(13, 0), new TimeOnly(15, 0)),
                                          (new TimeOnly(10, 0), new TimeOnly(15, 0)));
        Assert.Equal("10:00", grown.Start.ToString("HH:mm"));
        Assert.Equal("15:00", grown.End.ToString("HH:mm"));

        // already covering row is left untouched
        var kept = WicWorkingShift.Widen((new TimeOnly(7, 30), new TimeOnly(16, 30)),
                                         (new TimeOnly(10, 0), new TimeOnly(15, 0)));
        Assert.Equal("07:30", kept.Start.ToString("HH:mm"));
        Assert.Equal("16:30", kept.End.ToString("HH:mm"));
    }

    [Fact]
    public void WidenDaily_OneCentre_ChangesNothing() =>
        Assert.Null(WicWorkingShift.WidenDaily("13:00", "15:00", new[] { "13:00-15:00" }));

    [Fact]
    public void WidenDaily_TwoCentres_ReturnsTheSpan()
    {
        var widened = WicWorkingShift.WidenDaily("13:00", "15:00", new[] { "10:00-12:00", "13:00-15:00" });
        Assert.NotNull(widened);
        Assert.Equal("10:00", widened!.Value.Start);
        Assert.Equal("15:00", widened.Value.End);
    }

    // ── Point 4: an empty intersection falls back to THAT centre's hours ─────────

    [Fact]
    public void FallbackWindow_IsTheCentreWindow() =>
        Assert.Equal("10:00-12:00", WicWorkingShift.FallbackWindow("10:00", "12:00"));

    [Fact]
    public void FallbackWindow_NoCentreWindow_IsNull() =>
        Assert.Null(WicWorkingShift.FallbackWindow(null, null));

    // ── Point 5 scenario: 10-12 A + 13-15 B -> 10:00-15:00, two WIC rows, no conflict ──

    [Fact]
    public async Task AssignAgent_SecondCentre_WidensDailyShiftToTheSpan_AndKeepsTwoWicRows()
    {
        using var db = NewDb();
        SeedTwoCentres(db);
        // The state the RTM report starts from: the agent's own row is 13:00-15:00
        // (Woldeforster), then Am Hanseufer 10:00-12:00 is added on the same day.
        db.ShiftEntries.Add(DailyShift(db, "13:00", "15:00"));
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));

        // First centre: A. Its WIC row must be A's hours, NOT the agent's 13:00-15:00.
        var first = await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", CodeA, Thu.ToString("yyyy-MM-dd"), null, null), db);
        Assert.NotNull(first);

        var rowA = await db.WicShiftEntries.SingleAsync(w => w.SupportLocation == NameA);
        Assert.Equal("10:00-12:00", rowA.WorkingShift);         // point 4
        // Still a single-centre day, so the daily row is left alone here.
        var afterFirst = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E1");
        Assert.Equal("13:00", afterFirst.ShiftStart);
        Assert.Equal("15:00", afterFirst.ShiftEnd);

        // Second centre: B. Its row is B's hours and the daily shift grows to the span.
        var second = await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", CodeB, Thu.ToString("yyyy-MM-dd"), null, null), db);
        Assert.NotNull(second);

        var rows = await db.WicShiftEntries.Where(w => w.IsOnSite).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("10:00-12:00", rows.Single(w => w.SupportLocation == NameA).WorkingShift);
        Assert.Equal("13:00-15:00", rows.Single(w => w.SupportLocation == NameB).WorkingShift);

        var day = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E1");
        Assert.Equal("10:00", day.ShiftStart);
        Assert.Equal("15:00", day.ShiftEnd);

        // ... and the two centres are NOT a conflict on the trimmed per-centre windows.
        Assert.Empty(await new WicConflictDetector(db).GetConflictsAsync(Thu, Thu));
        var split = Assert.Single(await new WicConflictDetector(db).GetConflictsAsync(Thu, Thu, includeSplitShifts: true));
        Assert.Equal("SPLIT_SHIFT", split.ConflictType);
    }

    [Fact]
    public async Task AssignAgent_SecondCentre_DoesNotNarrowAnAlreadyWiderDailyShift()
    {
        using var db = NewDb();
        SeedTwoCentres(db);
        db.ShiftEntries.Add(DailyShift(db, "07:30", "16:30"));
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", CodeA, Thu.ToString("yyyy-MM-dd"), null, null), db);

        // 07:30-16:30 already covers 10:00-15:00 — the rule widens, it never narrows.
        var day = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E1");
        Assert.Equal("07:30", day.ShiftStart);
        Assert.Equal("16:30", day.ShiftEnd);
        Assert.Equal("10:00-12:00", (await db.WicShiftEntries.SingleAsync(w => w.SupportLocation == NameA)).WorkingShift);
    }

    [Fact]
    public async Task CreateShift_NewCentreOnAnExistingMultiCentreDay_WidensTheDailyShift()
    {
        using var db = NewDb();
        SeedTwoCentres(db);
        db.ShiftEntries.Add(DailyShift(db, "13:00", "15:00"));
        db.WicShiftEntries.Add(WicRow(NameB, "13:00-15:00"));
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        await svc.CreateShiftAsync(
            new CreateShiftRequest("E1", Thu.ToString("yyyy-MM-dd"), ShiftTypes.WicDuty, null, null, null, CodeA),
            db, new ShiftValidationService(db), "TEST-OPERATOR");

        Assert.Equal("10:00-12:00", (await db.WicShiftEntries.SingleAsync(w => w.SupportLocation == NameA)).WorkingShift);
        Assert.Equal("13:00-15:00", (await db.WicShiftEntries.SingleAsync(w => w.SupportLocation == NameB)).WorkingShift);
        var day = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E1");
        Assert.Equal("10:00", day.ShiftStart);
        Assert.Equal("15:00", day.ShiftEnd);
    }

    // ── Point 3: a Shift Plan cell edit for ONE centre cannot narrow the day ─────

    [Fact]
    public async Task ShiftPlanCellEdit_OneCentre_CannotNarrowTheDailyShift()
    {
        using var db = NewDb();
        SeedTwoCentres(db);
        db.ShiftEntries.Add(DailyShift(db, "10:00", "15:00"));
        db.WicShiftEntries.Add(WicRow(NameA, "10:00-12:00"));
        db.WicShiftEntries.Add(WicRow(NameB, "13:00-15:00"));
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        // The user edits the Woldeforster cell down to 13:00-15:00 — the Am Hanseufer
        // 10:00-12:00 part must not fall out of the day.
        var result = await svc.UpdateShiftAsync(501,
            new ShiftUpdateDto(ShiftTypes.WicDuty, "13:00", "15:00", null, CodeB, null), "TEST-OPERATOR");

        Assert.Null(result.Error);
        var day = await db.ShiftEntries.SingleAsync(s => s.Id == 501);
        Assert.Equal("10:00", day.ShiftStart);
        Assert.Equal("15:00", day.ShiftEnd);
        Assert.Equal("10:00-12:00", (await db.WicShiftEntries.SingleAsync(w => w.SupportLocation == NameA)).WorkingShift);
        Assert.Equal("13:00-15:00", (await db.WicShiftEntries.SingleAsync(w => w.SupportLocation == NameB)).WorkingShift);
    }

    [Fact]
    public async Task ShiftPlanCellEdit_SingleCentreDay_KeepsTheEditedTimes()
    {
        using var db = NewDb();
        SeedTwoCentres(db);
        db.ShiftEntries.Add(DailyShift(db, "10:00", "15:00"));
        db.WicShiftEntries.Add(WicRow(NameA, "10:00-12:00"));
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.UpdateShiftAsync(501,
            new ShiftUpdateDto(ShiftTypes.WicDuty, "10:30", "11:30", null, CodeA, null), "TEST-OPERATOR");

        Assert.Null(result.Error);
        var day = await db.ShiftEntries.SingleAsync(s => s.Id == 501);
        Assert.Equal("10:30", day.ShiftStart);   // a single-centre day is not widened
        Assert.Equal("11:30", day.ShiftEnd);
    }

    // ── Point 4 for the Shift Plan writer ───────────────────────────────────────

    [Fact]
    public async Task ShiftPlanCellEdit_DisjointCentreForOneOfTwoRows_WritesThatCentresHours()
    {
        using var db = NewDb();
        SeedTwoCentres(db);
        // The day already covers B; the cell is edited for A with times that do not touch
        // A's 10:00-12:00 window. The A row must carry A's hours, not the cell's times.
        db.ShiftEntries.Add(DailyShift(db, "13:00", "15:00"));
        db.WicShiftEntries.Add(WicRow(NameB, "13:00-15:00"));
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.UpdateShiftAsync(501,
            new ShiftUpdateDto(ShiftTypes.WicDuty, "13:00", "15:00", null, CodeA, null), "TEST-OPERATOR");

        Assert.Null(result.Error);
        Assert.Equal("10:00-12:00", (await db.WicShiftEntries.SingleAsync(w => w.SupportLocation == NameA)).WorkingShift);
        var day = await db.ShiftEntries.SingleAsync(s => s.Id == 501);
        Assert.Equal("10:00", day.ShiftStart);
        Assert.Equal("15:00", day.ShiftEnd);
    }
}
