using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// Regression tests for the POST /api/wic/assignments overlap check
// (WicShiftService.CreateAssignmentAsync). Only IsOnSite=true rows should count
// as a live time conflict — see BLUEPRINT_LOGIC.md §7.2.
public class CreateAssignmentOverlapTests
{
    private static GSDContext NewDb()
    {
        var options = new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GSDContext(options);
    }

    private static void SeedEmployee(GSDContext db, string employeeId) =>
        db.Employees.Add(new Employee { EmployeeId = employeeId, FullName = "Test Agent", IsActive = true });

    private static void SeedLocation(GSDContext db, string code, string displayName, DateOnly date, string open, string close)
    {
        db.WicLocations.Add(new WicLocation { LocationCode = code, DisplayName = displayName, IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = code,
            DayOfWeek = (int)date.DayOfWeek,
            OpenTime = open,
            CloseTime = close,
            IsClosed = false,
        });
    }

    private static object GetValue(IResult result) =>
        ((IValueHttpResult)result).Value!;

    private static string? GetString(object value, string propertyName) =>
        (string?)value.GetType().GetProperty(propertyName)?.GetValue(value);

    private static bool GetBool(object value, string propertyName) =>
        (bool)(value.GetType().GetProperty(propertyName)?.GetValue(value) ?? false);

    // (a) Agent moved to GSD on a date (WicShiftEntries row left as IsOnSite=false,
    // IsGSDDay=true history), then assigned to another WIC location with overlapping
    // hours -> succeeds; the GSD-history row must not block it.
    [Fact]
    public async Task Assignment_Succeeds_WhenOnlyOverlappingRowIsGsdHistory()
    {
        using var db = NewDb();
        var date = new DateOnly(2026, 9, 28); // Monday
        const string empId = "E1";

        SeedEmployee(db, empId);
        SeedLocation(db, "LOC_B", "Loc B", date, "08:00", "17:00");
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = empId,
            ShiftDate = date,
            SupportLocation = "Loc A",
            WorkingShift = "08:00-17:00",
            IsOnSite = false,
            IsGSDDay = true,
        });
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        var result = await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest(empId, "LOC_B", "2026-09-28", null, null), db);

        var value = GetValue(result);
        Assert.True(GetBool(value, "success"));
        Assert.Null(value.GetType().GetProperty("skipped"));
        Assert.Equal("Loc B", GetString(value, "displayName"));
    }

    // (b) Agent with an active IsOnSite=1 overlapping assignment -> still blocked.
    [Fact]
    public async Task Assignment_Blocked_WhenOverlappingRowIsOnSite()
    {
        using var db = NewDb();
        var date = new DateOnly(2026, 9, 28); // Monday
        const string empId = "E2";

        SeedEmployee(db, empId);
        SeedLocation(db, "LOC_B", "Loc B", date, "08:00", "17:00");
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = empId,
            ShiftDate = date,
            SupportLocation = "Loc A",
            WorkingShift = "08:00-17:00",
            IsOnSite = true,
        });
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        var result = await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest(empId, "LOC_B", "2026-09-28", null, null), db);

        var value = GetValue(result);
        Assert.True(GetBool(value, "success"));
        Assert.True(GetBool(value, "skipped"));
        Assert.Contains("Time conflict", GetString(value, "reason"));
    }

    // (c) Blocking shift type (e.g. AL) on the date -> still blocked, regardless of
    // the IsOnSite filter change.
    [Fact]
    public async Task Assignment_Blocked_WhenAgentHasBlockingShiftType()
    {
        using var db = NewDb();
        var date = new DateOnly(2026, 9, 28); // Monday
        const string empId = "E3";

        SeedEmployee(db, empId);
        SeedLocation(db, "LOC_B", "Loc B", date, "08:00", "17:00");
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = empId, ShiftDate = date, ShiftType = "AL" });
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        var result = await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest(empId, "LOC_B", "2026-09-28", null, null), db);

        var value = GetValue(result);
        Assert.True(GetBool(value, "success"));
        Assert.True(GetBool(value, "skipped"));
        Assert.Contains("shift type 'AL'", GetString(value, "reason"));
    }
}
