using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// The Shift Plan's MAIN fallback (ShiftService.SyncWicDutyFromShiftPlanAsync): a
// human WIC_DUTY edit that names no centre must land in the agent's MAIN centre that
// is OPEN on the edited DATE — the same per-date rule the roster generator uses
// (WicHoursResolver.IsOpenOn). The old code took the first MAIN row whatever weekday
// it was, so a Tuesday-only MAIN centre absorbed a Wednesday edit. When NO MAIN
// centre is open that day the sync writes no WIC row and returns the machine reason
// code "NO_OPEN_MAIN" instead of inventing a centre.
public class SyncWicDutyMainFallbackTests
{
    private const string Emp  = "E1";
    private const string Name = "Main Fallback Agent";

    private static readonly DateOnly Monday    = new(2026, 10, 5);
    private static readonly DateOnly Wednesday = new(2026, 10, 7);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // MAIN1 opens Monday only; MAIN2 opens Wednesday only. MAIN1 has the lower Id,
    // so the old first-row-wins code would always have picked MAIN1.
    private static void Seed(GSDContext db)
    {
        db.Employees.Add(new Employee { EmployeeId = Emp, FullName = Name, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "MAIN1", DisplayName = "Main One", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "MAIN2", DisplayName = "Main Two", IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "MAIN1", DayOfWeek = (int)DayOfWeek.Monday,    OpenTime = "08:00", CloseTime = "12:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "MAIN2", DayOfWeek = (int)DayOfWeek.Wednesday, OpenTime = "09:00", CloseTime = "17:00" });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 1, LocationCode = "MAIN1", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 2, LocationCode = "MAIN2", EmployeeName = Name, AssignmentType = "MAIN", IsActive = true });
    }

    private static ShiftEntry SeedWorkingShift(GSDContext db, DateOnly date, int id)
    {
        var row = new ShiftEntry { Id = id, EmployeeId = Emp, ShiftDate = date, ShiftType = ShiftTypes.Working };
        db.ShiftEntries.Add(row);
        return row;
    }

    [Fact]
    public async Task MainFallback_OnAnOpenMainDay_UsesThatDaysCentre_NotTheFirstMainRow()
    {
        using var db = NewDb();
        Seed(db);
        SeedWorkingShift(db, Wednesday, 501);
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.UpdateShiftAsync(501,
            new ShiftUpdateDto(ShiftTypes.WicDuty, null, null, null, null, null), "TEST-OPERATOR");

        Assert.Null(result.Error);
        Assert.Null(result.Warning);

        // Wednesday: MAIN2 is the open centre, even though MAIN1 has the lower Id.
        var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == Emp && w.ShiftDate == Wednesday);
        Assert.Equal("Main Two", wic.SupportLocation);
        Assert.True(wic.IsOnSite);
        Assert.Equal("09:00-17:00", wic.WorkingShift);
    }

    [Fact]
    public async Task MainFallback_NoMainOpenThatDay_WritesNoWicRow_AndReturnsNoOpenMain()
    {
        using var db = NewDb();
        Seed(db);
        // Tuesday: neither MAIN1 (Monday) nor MAIN2 (Wednesday) is open.
        SeedWorkingShift(db, new DateOnly(2026, 10, 6), 502);
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.UpdateShiftAsync(502,
            new ShiftUpdateDto(ShiftTypes.WicDuty, "08:00", "16:00", null, null, null), "TEST-OPERATOR");

        Assert.Null(result.Error);
        Assert.Equal("NO_OPEN_MAIN", result.Warning);
        // The Shift Plan cell is still saved as WIC duty...
        Assert.Equal(ShiftTypes.WicDuty, (await db.ShiftEntries.SingleAsync(s => s.Id == 502)).ShiftType);
        // ...but no WIC row is invented for a day no MAIN centre is open.
        Assert.Empty(await db.WicShiftEntries.Where(w => w.EmployeeId == Emp).ToListAsync());
    }
}
