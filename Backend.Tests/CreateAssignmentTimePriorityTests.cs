using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// Regression tests for the ASSIGN time-source priority in
// WicShiftService.CreateAssignmentAsync (fixes the Helmstedt/Rendsburg pattern
// where the ShiftEntry got the location opening hours instead of the agent's time):
//   1. explicit request time (req.ShiftStart/ShiftEnd)
//   2. the agent's existing WorkingShift on WicShiftEntries for this date
//      (e.g. imported "09:00-17:00" rows with IsOnSite=false)
//   3. the opening hours resolved FOR THIS DATE via WicHoursResolver
public class CreateAssignmentTimePriorityTests
{
    private static readonly DateOnly Date = new(2026, 9, 28); // Monday

    private static GSDContext NewDb()
    {
        var options = new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GSDContext(options);
    }

    private static void SeedEmployee(GSDContext db, string employeeId) =>
        db.Employees.Add(new Employee { EmployeeId = employeeId, FullName = "Test Agent", IsActive = true });

    private static void SeedLocation(GSDContext db, string code, string displayName) =>
        db.WicLocations.Add(new WicLocation { LocationCode = code, DisplayName = displayName, IsActive = true });

    private static void SeedHours(GSDContext db, string code, string open, string close, DateOnly? effectiveFrom = null) =>
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = code,
            DayOfWeek = (int)Date.DayOfWeek,
            OpenTime = open,
            CloseTime = close,
            IsClosed = false,
            EffectiveFrom = effectiveFrom,
        });

    // Imported "Global Service Desk" row: carries the agent's real working time
    // but is not a live on-site commitment (IsOnSite=false), so it must not block
    // and SHOULD supply the assignment time.
    private static void SeedPriorWorkingShift(GSDContext db, string employeeId, string workingShift) =>
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = employeeId,
            ShiftDate = Date,
            SupportLocation = "Global Service Desk",
            WorkingShift = workingShift,
            IsOnSite = false,
        });

    // The agent's own schedule row for the date, written before the WIC assignment.
    private static void SeedAgentShiftEntry(GSDContext db, string employeeId,
        string start, string end, string shiftType = ShiftTypes.Working) =>
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = employeeId,
            ShiftDate = Date,
            ShiftType = shiftType,
            ShiftStart = start,
            ShiftEnd = end,
        });

    // A manual exception day for this exact date (overrides the Monday pattern).
    private static void SeedExceptionHours(GSDContext db, string code,
        string? open, string? close, bool closed) =>
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = code,
            DayOfWeek = (int)Date.DayOfWeek,
            OpenTime = open,
            CloseTime = close,
            IsClosed = closed,
            ExceptionDate = Date,
            ManualChangeAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc),
        });

    private static string? ReasonCode(IResult result)
    {
        var value = ((IValueHttpResult)result).Value!;
        return (string?)value.GetType().GetProperty("reasonCode")?.GetValue(value);
    }

    private static async Task<IResult> Assign(GSDContext db, string empId, string locCode, string? start = null, string? end = null)
    {
        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        return await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest(empId, locCode, Date.ToString("yyyy-MM-dd"), start, end), db);
    }

    private static void AssertSuccess(IResult result)
    {
        var value = ((IValueHttpResult)result).Value!;
        Assert.True((bool)(value.GetType().GetProperty("success")?.GetValue(value) ?? false));
        Assert.Null(value.GetType().GetProperty("skipped"));
    }

    // 1. Explicit request time beats both the prior WorkingShift and opening hours.
    [Fact]
    public async Task RequestTime_Wins_OverWorkingShiftAndOpeningHours()
    {
        using var db = NewDb();
        SeedEmployee(db, "E1");
        SeedLocation(db, "LOC_A", "Loc A");
        SeedHours(db, "LOC_A", "08:00", "18:00");
        SeedPriorWorkingShift(db, "E1", "09:00-17:00");
        await db.SaveChangesAsync();

        var result = await Assign(db, "E1", "LOC_A", "10:00", "14:00");

        AssertSuccess(result);
        var entry = db.WicShiftEntries.Single(w => w.SupportLocation == "Loc A");
        Assert.Equal("10:00-14:00", entry.WorkingShift);
        var shift = db.ShiftEntries.Single(s => s.EmployeeId == "E1");
        Assert.Equal("10:00", shift.ShiftStart);
        Assert.Equal("14:00", shift.ShiftEnd);
    }

    // 2. Helmstedt case: the agent's imported WorkingShift ("09:00-17:00") beats the
    //    location's opening hours (06:00-18:00) when no request time is given.
    [Fact]
    public async Task WorkingShift_Wins_OverOpeningHours_HelmstedtCase()
    {
        using var db = NewDb();
        SeedEmployee(db, "E2");
        SeedLocation(db, "HELM", "Helmstedt");
        SeedHours(db, "HELM", "06:00", "18:00");
        SeedPriorWorkingShift(db, "E2", "09:00-17:00");
        await db.SaveChangesAsync();

        var result = await Assign(db, "E2", "HELM");

        AssertSuccess(result);
        var entry = db.WicShiftEntries.Single(w => w.SupportLocation == "Helmstedt");
        Assert.Equal("09:00-17:00", entry.WorkingShift);
        var shift = db.ShiftEntries.Single(s => s.EmployeeId == "E2");
        Assert.Equal("09:00", shift.ShiftStart);
        Assert.Equal("17:00", shift.ShiftEnd);
    }


    // 3. No request time and no prior WorkingShift -> the opening hours resolved
    //    FOR THAT DATE win (EffectiveFrom 2026-09-01 row beats the null-EffectiveFrom row).
    [Fact]
    public async Task ResolvedOpeningHours_Used_WhenNoRequestTimeAndNoWorkingShift()
    {
        using var db = NewDb();
        SeedEmployee(db, "E3");
        SeedLocation(db, "LOC_C", "Loc C");
        SeedHours(db, "LOC_C", "08:00", "17:00");                           // old/default row
        SeedHours(db, "LOC_C", "09:30", "15:30", new DateOnly(2026, 9, 1)); // effective for 2026-09-28
        await db.SaveChangesAsync();

        var result = await Assign(db, "E3", "LOC_C");

        AssertSuccess(result);
        var entry = db.WicShiftEntries.Single(w => w.SupportLocation == "Loc C");
        Assert.Equal("09:30-15:30", entry.WorkingShift);
    }

    // 4. Two-block WorkingShift "07:00-12:00 / 12:30-13:30" spans the first block's
    //    start through the last block's end ("07:00-13:30"), not a broken parse.
    [Fact]
    public async Task TwoBlock_WorkingShift_Spans_FirstStart_To_LastEnd()
    {
        using var db = NewDb();
        SeedEmployee(db, "E4");
        SeedLocation(db, "LOC_D", "Loc D");
        SeedHours(db, "LOC_D", "06:00", "18:00");
        SeedPriorWorkingShift(db, "E4", "07:00-12:00 / 12:30-13:30");
        await db.SaveChangesAsync();

        var result = await Assign(db, "E4", "LOC_D");

        AssertSuccess(result);
        var entry = db.WicShiftEntries.Single(w => w.SupportLocation == "Loc D");
        Assert.Equal("07:00-13:30", entry.WorkingShift);
        var shift = db.ShiftEntries.Single(s => s.EmployeeId == "E4");
        Assert.Equal("07:00", shift.ShiftStart);
        Assert.Equal("13:30", shift.ShiftEnd);
    }

    // 5. NEW (2026-10-13): RTM assigns without a time while the agent already has his own
    //    ShiftEntries window. That window is KEPT on ShiftEntries, and the WIC row carries
    //    the intersection with the centre's hours — the centre's hours never become the
    //    agent's shift.
    [Fact]
    public async Task AgentShiftEntriesWindow_IsKept_AndWicRowGetsIntersection()
    {
        using var db = NewDb();
        SeedEmployee(db, "E5");
        SeedLocation(db, "LOC_E", "Loc E");
        SeedHours(db, "LOC_E", "06:00", "18:00");
        SeedAgentShiftEntry(db, "E5", "08:00", "17:00");
        await db.SaveChangesAsync();

        var result = await Assign(db, "E5", "LOC_E");

        AssertSuccess(result);
        var entry = db.WicShiftEntries.Single(w => w.SupportLocation == "Loc E");
        Assert.Equal("08:00-17:00", entry.WorkingShift);
        var shift = db.ShiftEntries.Single(s => s.EmployeeId == "E5");
        Assert.Equal(ShiftTypes.WicDuty, shift.ShiftType);
        Assert.Equal("08:00", shift.ShiftStart);
        Assert.Equal("17:00", shift.ShiftEnd);
    }

    // 6. The agent's shift reaches beyond the centre: the WIC row is cut to the centre's
    //    window (the intersection), while ShiftEntries keeps the full agent shift.
    [Fact]
    public async Task AgentShift_WiderThanCentre_WicRowIsCutToTheCentreWindow()
    {
        using var db = NewDb();
        SeedEmployee(db, "E6");
        SeedLocation(db, "LOC_F", "Loc F");
        SeedHours(db, "LOC_F", "06:00", "14:00");
        SeedAgentShiftEntry(db, "E6", "08:00", "17:00");
        await db.SaveChangesAsync();

        var result = await Assign(db, "E6", "LOC_F");

        AssertSuccess(result);
        var entry = db.WicShiftEntries.Single(w => w.SupportLocation == "Loc F");
        Assert.Equal("08:00-14:00", entry.WorkingShift);
        var shift = db.ShiftEntries.Single(s => s.EmployeeId == "E6");
        Assert.Equal("08:00", shift.ShiftStart);
        Assert.Equal("17:00", shift.ShiftEnd);
    }

    // 7. An explicit request time still wins over the agent's own ShiftEntries window.
    [Fact]
    public async Task ExplicitRequest_StillBeatsTheAgentShiftEntriesWindow()
    {
        using var db = NewDb();
        SeedEmployee(db, "E7");
        SeedLocation(db, "LOC_G", "Loc G");
        SeedHours(db, "LOC_G", "06:00", "18:00");
        SeedAgentShiftEntry(db, "E7", "08:00", "17:00");
        await db.SaveChangesAsync();

        var result = await Assign(db, "E7", "LOC_G", "10:00", "12:00");

        AssertSuccess(result);
        var entry = db.WicShiftEntries.Single(w => w.SupportLocation == "Loc G");
        Assert.Equal("10:00-12:00", entry.WorkingShift);
        var shift = db.ShiftEntries.Single(s => s.EmployeeId == "E7");
        Assert.Equal("10:00", shift.ShiftStart);
        Assert.Equal("12:00", shift.ShiftEnd);
    }

    // 8. An OPEN manual exception on a normally-closed weekday is honoured: the assignment
    //    is saved with the exception's window (intersected with the agent's own shift).
    [Fact]
    public async Task OpenExceptionDay_IsSaved_WithTheExceptionHours()
    {
        using var db = NewDb();
        SeedEmployee(db, "E8");
        SeedLocation(db, "LOC_H", "Loc H");
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = "LOC_H", DayOfWeek = (int)Date.DayOfWeek, IsClosed = true,
        });
        SeedExceptionHours(db, "LOC_H", "10:00", "16:00", closed: false);
        SeedAgentShiftEntry(db, "E8", "08:00", "20:00");
        await db.SaveChangesAsync();

        var result = await Assign(db, "E8", "LOC_H");

        AssertSuccess(result);
        var entry = db.WicShiftEntries.Single(w => w.SupportLocation == "Loc H");
        Assert.Equal("10:00-16:00", entry.WorkingShift);
    }

    // 9. A CLOSED manual exception on a normally-open weekday is still refused: the
    //    resolver puts the exception row before the pattern, then IsClosed.
    [Fact]
    public async Task ClosedExceptionDay_IsRefused_EvenWhenThePatternIsOpen()
    {
        using var db = NewDb();
        SeedEmployee(db, "E9");
        SeedLocation(db, "LOC_I", "Loc I");
        SeedHours(db, "LOC_I", "09:00", "17:00");
        SeedExceptionHours(db, "LOC_I", null, null, closed: true);
        await db.SaveChangesAsync();

        var result = await Assign(db, "E9", "LOC_I");

        Assert.Equal("CLOSED", ReasonCode(result));
        var value = ((IValueHttpResult)result).Value!;
        Assert.False((bool)(value.GetType().GetProperty("saved")?.GetValue(value) ?? true));
        Assert.Empty(db.WicShiftEntries.Where(w => w.EmployeeId == "E9"));
    }
}

