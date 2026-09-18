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
}

