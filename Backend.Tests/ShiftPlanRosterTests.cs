using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.Tests;

// The Shift Plan selects PEOPLE, not shift rows. Until 2026-10-15 the list was an
// INNER JOIN (ShiftEntries x Employees), so an active employee with no shift row inside
// the requested window was not a row at all.
//
// Live case: employee 9128154 was created on 2026-10-14 with a single row dated that
// day. On 2026-10-15 the default 7-day plan started on 2026-10-15, the 14.10 row fell
// out of the window, and the employee disappeared from the plan even though
// Employees.IsActive = 1 and /api/employees returned them.
//
// These tests pin the new rule: every active Employees row is a plan row, and an
// employee with no row in the window gets exactly one placeholder (Id 0, EMPTY).
public class ShiftPlanRosterTests
{
    private static readonly DateOnly Wed = new(2026, 10, 14);
    private static readonly DateOnly Thu = new(2026, 10, 15);
    private static readonly DateOnly Sun = new(2026, 10, 18);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ShiftService NewSvc(GSDContext db) => new(db);

    private static void SeedEmployee(GSDContext db, string id, string name, string role,
        string? teamLead = "TL One", string engagement = "Full Time", bool active = true) =>
        db.Employees.Add(new Employee
        {
            EmployeeId = id, FirstName = name, LastName = "", FullName = name,
            Engagement = engagement, PrimaryRole = role, TeamLeadName = teamLead,
            IsActive = active, SourceSheet = "GSD_WIC",
        });

    private static void SeedShift(GSDContext db, string id, DateOnly date, string type,
        string? start = null, string? end = null) =>
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = id, ShiftDate = date, ShiftType = type, ShiftStart = start, ShiftEnd = end,
        });

    private static ShiftFilterParams Window(string from, string to,
        string? teamLead = null, string? role = null, string? engagement = null, string? shiftType = null) =>
        new(from, to, teamLead, role, engagement, shiftType);

    [Fact]
    public async Task ActiveEmployeeWithNoShiftRows_IsStillARowInThePlan()
    {
        using var db = NewDb();
        SeedEmployee(db, "9128154", "New Agent", "WIC");
        await db.SaveChangesAsync();

        var rows = await NewSvc(db).GetShiftsAsync(Window("2026-10-15", "2026-10-21"));

        var row = Assert.Single(rows);
        Assert.Equal("9128154", row.EmployeeId);
        Assert.Equal(0, row.Id);
        Assert.Equal(ShiftTypes.Empty, row.ShiftType);
        Assert.Equal(Thu, row.ShiftDate);
        // A placeholder is not a task: it must never publish a default task badge.
        Assert.Null(row.EffectiveTask);
        Assert.Equal("New Agent", row.FullName);
        Assert.Equal("WIC", row.PrimaryRole);
        Assert.Equal("TL One", row.TeamLeadName);
    }

    // The exact live shape: the only row is the day BEFORE the window.
    [Fact]
    public async Task EmployeeWhoseOnlyRowIsOutsideTheWindow_IsStillARow()
    {
        using var db = NewDb();
        SeedEmployee(db, "9128154", "New Agent", "WIC");
        SeedShift(db, "9128154", Wed, ShiftTypes.UnpaidLeave, "08:00", "17:00");
        SeedEmployee(db, "1111111", "Known Agent", "WIC");
        SeedShift(db, "1111111", Thu, ShiftTypes.Working, "08:00", "17:00");
        await db.SaveChangesAsync();

        var rows = await NewSvc(db).GetShiftsAsync(Window("2026-10-15", "2026-10-21"));

        Assert.Equal(2, rows.Count);
        var placeholder = Assert.Single(rows, r => r.EmployeeId == "9128154");
        Assert.Equal(0, placeholder.Id);
        Assert.Equal(ShiftTypes.Empty, placeholder.ShiftType);
        // The known agent keeps their real row, and gets no extra placeholder.
        var real = Assert.Single(rows, r => r.EmployeeId == "1111111");
        Assert.NotEqual(0, real.Id);
        Assert.Equal(ShiftTypes.Working, real.ShiftType);
    }

    [Fact]
    public async Task EmployeeWithRowsInTheWindow_GetsNoPlaceholderExtra()
    {
        using var db = NewDb();
        SeedEmployee(db, "2222222", "Two Days", "WIC");
        SeedShift(db, "2222222", Thu, ShiftTypes.Working, "08:00", "17:00");
        SeedShift(db, "2222222", Sun, ShiftTypes.Off);
        await db.SaveChangesAsync();

        var rows = await NewSvc(db).GetShiftsAsync(Window("2026-10-15", "2026-10-21"));

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.Id == 0);
    }

    [Fact]
    public async Task InactiveEmployee_IsNotInThePlan()
    {
        using var db = NewDb();
        SeedEmployee(db, "3333333", "Left", "WIC", active: false);
        SeedShift(db, "3333333", Thu, ShiftTypes.Resigned);
        await db.SaveChangesAsync();

        var rows = await NewSvc(db).GetShiftsAsync(Window("2026-10-15", "2026-10-21"));

        Assert.Empty(rows);
    }

    // The shift-type filter still selects ROWS, so an employee without such a row drops
    // out exactly as before - "WIC_DUTY only" must not list everybody as a placeholder.
    [Fact]
    public async Task ShiftTypeFilter_DropsTheEmployeeWithoutSuchARow()
    {
        using var db = NewDb();
        SeedEmployee(db, "4444444", "No WIC", "WIC");
        SeedEmployee(db, "5555555", "On WIC", "WIC");
        SeedShift(db, "5555555", Thu, ShiftTypes.WicDuty, "08:00", "17:00");
        await db.SaveChangesAsync();

        var rows = await NewSvc(db).GetShiftsAsync(Window("2026-10-15", "2026-10-21", shiftType: ShiftTypes.WicDuty));

        var row = Assert.Single(rows);
        Assert.Equal("5555555", row.EmployeeId);
    }

    [Fact]
    public async Task TeamLeadAndRoleFilters_StillSelectEmployees()
    {
        using var db = NewDb();
        SeedEmployee(db, "6666666", "Alpha", "WIC", teamLead: "TL One");
        SeedEmployee(db, "7777777", "Beta", "WIC", teamLead: "TL Two");
        SeedEmployee(db, "8888888", "Gamma", "Voice", teamLead: "TL One");
        await db.SaveChangesAsync();

        var rows = await NewSvc(db).GetShiftsAsync(Window("2026-10-15", "2026-10-21", teamLead: "TL One", role: "WIC"));

        var row = Assert.Single(rows);
        Assert.Equal("6666666", row.EmployeeId);
    }

    [Fact]
    public async Task SingleDayWindow_ReturnsEveryActiveEmployeeOnce()
    {
        using var db = NewDb();
        SeedEmployee(db, "9999999", "Only One", "WIC");
        await db.SaveChangesAsync();

        var rows = await NewSvc(db).GetShiftsAsync(Window("2026-10-15", "2026-10-15"));

        var row = Assert.Single(rows);
        Assert.Equal(Thu, row.ShiftDate);
        Assert.Equal(0, row.Id);
    }
}
