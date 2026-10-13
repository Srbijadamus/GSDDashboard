using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace GSDDashboard.Tests;

// Writer #1 (the RosterService batch writer): a WIC batch without an explicit shift
// time is REFUSED in ValidateAsync (code WIC_SHIFT_TIME_REQUIRED) and the centre's
// opening hours are NEVER written into ShiftEntries.ShiftStart/ShiftEnd.
//
// Live state that motivated it (2026-10-13): 1138 open WIC_DUTY rows have no
// WicShiftEntries row and every one of them came from SourceModule="Roster"; 925 of
// them carry exactly the centre's own window, because ValidateAsync left start/end
// null and BuildPlanAsync filled ShiftEntries with the centre's hours
// (Backend/RosterService.cs:763-764, 918-929, 1091-1092).
//
// The rules pinned here:
//   1. a WIC request (explicit LocationCode) without shiftStart/shiftEnd is refused;
//   2. so is the auto-detected-MAIN shape (no LocationCode) — it is still WIC mode;
//   3. only half of the pair is refused the same way, not with "Invalid shiftEnd";
//   4. a plain roster without times keeps its old plain message;
//   5. with an explicit shift, ShiftEntries carries the AGENT's shift and the
//      WicShiftEntries row carries that shift CUT TO the centre window.
public class RosterWicShiftTimeRequiredTests
{
    private const string Emp  = "E-WICTIME";
    private const string Name = "Wic Time Agent";
    private const string Code = "DE~X~Centre";
    private static readonly DateOnly Monday = new(2026, 10, 5);   // a Monday

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RosterService Svc(GSDContext db) =>
        new(db, new WicShiftService(db, new AvailabilityResolver(db)), new WicCoverageGapService(db));

    private static void SeedCentre(GSDContext db, string open = "08:00", string close = "12:00")
    {
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = Code, DisplayName = "Centre", IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = (int)DayOfWeek.Monday,
            OpenTime = open, CloseTime = close, IsClosed = false,
        });
    }

    private static RosterRequest WicReq(string? start, string? end) =>
        new(Emp, "2026-10-05", "2026-10-05", start, end, null, null, Code);

    // ── 1. refused: explicit centre, no time ─────────────────────────────────

    [Fact]
    public async Task WicPreview_WithoutShiftTime_IsRefusedWithTheMachineCode()
    {
        using var db = NewDb();
        SeedCentre(db);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<RosterValidationException>(() =>
            Svc(db).PreviewAsync(WicReq(null, null)));

        Assert.Equal(RosterErrorCodes.WicShiftTimeRequired, ex.Code);
        // A clear, English message that names both fields and the reason.
        Assert.Contains("shiftStart", ex.Message);
        Assert.Contains("shiftEnd", ex.Message);
        Assert.Contains("opening hours", ex.Message);
        // Nothing was planned and nothing written.
        Assert.False(await db.ShiftEntries.AnyAsync());
        Assert.False(await db.WicShiftEntries.AnyAsync());
    }

    [Fact]
    public async Task WicGenerate_WithoutShiftTime_IsRefusedBeforeAnythingIsWritten()
    {
        using var db = NewDb();
        SeedCentre(db);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<RosterValidationException>(() =>
            Svc(db).GenerateAsync(WicReq(null, null), "K1", "Tester"));

        Assert.Equal(RosterErrorCodes.WicShiftTimeRequired, ex.Code);
        Assert.False(await db.ShiftEntries.AnyAsync());
        Assert.False(await db.WicShiftEntries.AnyAsync());
        Assert.False(await db.RosterBatches.AnyAsync());
    }

    [Fact]
    public async Task WicPreview_OnlyHalfTheShift_IsRefusedWithTheSameCode()
    {
        using var db = NewDb();
        SeedCentre(db);
        await db.SaveChangesAsync();

        // Only a start: the pair is incomplete, so this is the WIC-time refusal and
        // NOT the generic "Invalid shiftEnd (expected HH:mm)".
        var ex = await Assert.ThrowsAsync<RosterValidationException>(() =>
            Svc(db).PreviewAsync(WicReq("09:00", null)));

        Assert.Equal(RosterErrorCodes.WicShiftTimeRequired, ex.Code);
    }

    // ── 2. the auto-detected-MAIN shape is WIC mode too ──────────────────────

    [Fact]
    public async Task WicPreview_AutoDetectedMain_WithoutShiftTime_IsRefused()
    {
        using var db = NewDb();
        SeedCentre(db);
        db.WicAgentAssignments.Add(new WicAgentAssignment
        {
            Id = 1, LocationCode = Code, EmployeeName = Name, AssignmentType = "MAIN", IsActive = true,
        });
        await db.SaveChangesAsync();

        // No LocationCode at all: the MAIN row makes this a WIC run as well.
        var ex = await Assert.ThrowsAsync<RosterValidationException>(() =>
            Svc(db).PreviewAsync(new RosterRequest(Emp, "2026-10-05", "2026-10-05", null, null, null, null)));

        Assert.Equal(RosterErrorCodes.WicShiftTimeRequired, ex.Code);
    }

    // ── 3. the plain roster keeps its own message ────────────────────────────

    [Fact]
    public async Task PlainRoster_WithoutShiftTime_KeepsThePlainMessage()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<RosterValidationException>(() =>
            Svc(db).PreviewAsync(new RosterRequest(Emp, "2026-10-05", "2026-10-05", null, null, null, null)));

        Assert.Null(ex.Code);                          // not a WIC refusal
        Assert.Contains("Invalid shiftStart", ex.Message);
    }

    // ── 4. the centre window never becomes the agent's ShiftEntries time ─────

    [Fact]
    public async Task WicGenerate_ShiftWiderThanCentre_ShiftEntryKeepsTheAgentShift_WicGetsTheSlice()
    {
        using var db = NewDb();
        SeedCentre(db, open: "10:00", close: "12:00");
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(
            WicReq("07:00", "16:00"), "K1", "Tester");

        Assert.Equal(1, g.WicDays);

        var se = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == Monday);
        Assert.Equal(ShiftTypes.WicDuty, se.ShiftType);
        Assert.Equal("07:00", se.ShiftStart);   // the AGENT's shift — never the centre's 10:00-12:00
        Assert.Equal("16:00", se.ShiftEnd);

        var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == Emp && w.ShiftDate == Monday);
        Assert.Equal("Centre", wic.SupportLocation);
        Assert.Equal("10:00-12:00", wic.WorkingShift);   // the agent's shift CUT TO the centre window
        Assert.True(wic.IsOnSite);
    }

    [Fact]
    public async Task WicGenerate_ShiftNarrowerThanCentre_WritesOnlyTheRequestedShift()
    {
        using var db = NewDb();
        SeedCentre(db, open: "08:00", close: "17:00");
        await db.SaveChangesAsync();

        var g = await Svc(db).GenerateAsync(WicReq("09:00", "11:00"), "K1", "Tester");
        Assert.Equal(1, g.WicDays);

        var se = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == Emp && s.ShiftDate == Monday);
        Assert.Equal("09:00", se.ShiftStart);
        Assert.Equal("11:00", se.ShiftEnd);

        var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == Emp && w.ShiftDate == Monday);
        Assert.Equal("09:00-11:00", wic.WorkingShift);
    }

    [Fact]
    public async Task WicPreview_WithShiftTime_IsAccepted()
    {
        using var db = NewDb();
        SeedCentre(db);
        await db.SaveChangesAsync();

        var p = await Svc(db).PreviewAsync(WicReq("09:00", "11:00"));

        var row = Assert.Single(p.Rows);
        Assert.Equal("WIC", row.Kind);
        Assert.Equal("09:00", row.ShiftStart);
        Assert.Equal("11:00", row.ShiftEnd);
    }
}
