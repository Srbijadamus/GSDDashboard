using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Vacations;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// Regression tests for POST /api/vacations with leaveType="OL" (VacationService.CreateAsync).
// OL reuses the same AL date-range picker but must not touch the Vacations table
// or AL balance, must allow past start dates, and must write one ShiftEntry per
// working day (Mon-Fri) in the range.
public class VacationOtherLeaveTests
{
    private static GSDContext NewDb()
    {
        var options = new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GSDContext(options);
    }

    private static void SeedEmployee(GSDContext db, string employeeId) =>
        db.Employees.Add(new Employee { EmployeeId = employeeId, FullName = "Test Agent", IsActive = true, SourceSheet = "DE" });

    [Fact]
    public async Task CreateAsync_OL_CreatesOneShiftEntryPerWorkingDay_NoVacationRow()
    {
        using var db = NewDb();
        const string empId = "E10";
        SeedEmployee(db, empId);
        await db.SaveChangesAsync();

        var svc = new VacationService(db, new ShiftSyncService(db));
        // Monday 2026-09-21 .. Sunday 2026-09-27 -> 5 working days (Mon-Fri), weekend skipped.
        var dto = new CreateVacationDto(empId, "2026-09-21", "2026-09-27", "Test OL", false, "OL");

        var result = await svc.CreateAsync(dto);

        Assert.NotNull(result);
        Assert.Equal(5, db.ShiftEntries.Count(x => x.EmployeeId == empId && x.ShiftType == "OL"));
        Assert.Empty(db.Vacations);
        Assert.Empty(db.ALBalances);
    }

    [Fact]
    public async Task CreateAsync_OL_AllowsPastStartDate()
    {
        using var db = NewDb();
        const string empId = "E11";
        SeedEmployee(db, empId);
        await db.SaveChangesAsync();

        var svc = new VacationService(db, new ShiftSyncService(db));
        var pastStart = DateOnly.FromDateTime(DateTime.Today).AddDays(-30).ToString("yyyy-MM-dd");
        var pastEnd   = DateOnly.FromDateTime(DateTime.Today).AddDays(-28).ToString("yyyy-MM-dd");
        var dto = new CreateVacationDto(empId, pastStart, pastEnd, null, false, "OL");

        var result = await svc.CreateAsync(dto);

        Assert.NotNull(result);
        Assert.True(db.ShiftEntries.Any(x => x.EmployeeId == empId && x.ShiftType == "OL"));
    }

    [Fact]
    public async Task CreateAsync_AL_StillWritesVacationRowAndBalance()
    {
        using var db = NewDb();
        const string empId = "E12";
        SeedEmployee(db, empId);
        await db.SaveChangesAsync();

        var svc = new VacationService(db, new ShiftSyncService(db));
        var dto = new CreateVacationDto(empId, "2026-09-21", "2026-09-25", null, false, "AL");

        var result = await svc.CreateAsync(dto);

        Assert.NotNull(result);
        Assert.Single(db.Vacations);
        Assert.True(db.ShiftEntries.Any(x => x.EmployeeId == empId && x.ShiftType == "AL"));
    }
}
