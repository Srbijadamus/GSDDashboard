using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// RTM 2026-10-15.
//
//   A) HALF_AL (a "HAL" day) is half annual leave and half WORK. A WIC duty may be placed
//      on the working part: the writers no longer answer BLOCKING_SHIFT, ShiftEntries stays
//      HALF_AL (its ShiftStart..ShiftEnd is the working part, which is what the HR export
//      and the Shift Plan show) and the WicShiftEntries row carries the intersection of
//      that part and the centre's window. Full AL/SL/UL and the other blocking types still
//      refuse; a centre staffed by a HALF_AL agent reads COVERED, not uncovered.
//
//   B) WIC ATTENDANCE FOLLOWS EVERY ShiftEntries WRITER: a row that leaves WIC_DUTY for
//      ANY other type — Shift Plan cell, Bulk RTM (SL through ShiftSyncService, AL/OFF
//      through the assign route), a BO/back-office day, the roster, imports — stands the
//      day's on-site WicShiftEntries rows down in the same SaveChanges. Centre A → centre B
//      (Assign Agent) replaces the old centre's row with the new one.
public class HalWicAndWicAttendanceLinkTests
{
    private const string HelmstedtCode = "DE~38350~Helmstedt~Schillerstr. 3";
    private const string Helmstedt     = "Helmstedt";

    // 2026-10-16 is a Friday; Helmstedt opens 08:00-13:00 that weekday.
    private static readonly DateOnly Fri = new(2026, 10, 16);
    // 2026-10-08 is a Thursday.
    private static readonly DateOnly Thu = new(2026, 10, 8);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static void SeedHelmstedt(GSDContext db)
    {
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Half Al Agent", IsActive = true });
        db.WicLocations.Add(new WicLocation
        {
            LocationCode = HelmstedtCode, DisplayName = Helmstedt, City = "Helmstedt",
            Country = "DE", IsActive = true, MinAgentsRequired = 1,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        { LocationCode = HelmstedtCode, DayOfWeek = (int)Fri.DayOfWeek, OpenTime = "08:00", CloseTime = "13:00" });
        db.WicOpeningHours.Add(new WicOpeningHour
        { LocationCode = HelmstedtCode, DayOfWeek = (int)Thu.DayOfWeek, OpenTime = "08:00", CloseTime = "17:00" });
    }

    private static void SeedTwoOverlappingCentres(GSDContext db)
    {
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Moved Agent", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "C-A", DisplayName = "Centre A", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = "C-B", DisplayName = "Centre B", IsActive = true });
        foreach (var code in new[] { "C-A", "C-B" })
            db.WicOpeningHours.Add(new WicOpeningHour
            { LocationCode = code, DayOfWeek = (int)Thu.DayOfWeek, OpenTime = "08:00", CloseTime = "17:00" });
    }

    // ── A) HALF_AL is allowed; the WIC row carries the intersection of the working part ──

    [Fact]
    public async Task HalfAlDay_AssignAgent_IsAllowed_AndTheWicRowIsTheIntersectionOfWorkingPartAndCentre()
    {
        using var db = NewDb();
        SeedHelmstedt(db);
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 1, EmployeeId = "E1", ShiftDate = Fri, ShiftType = ShiftTypes.HalfAL,
            ShiftStart = "08:00", ShiftEnd = "13:00", AgentTask = Helmstedt,
        });
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", HelmstedtCode, Fri.ToString("yyyy-MM-dd"), null, null),
            db, manualActor: "TEST-OPERATOR");

        // ShiftEntries STAYS HALF_AL with the working part as its own window (HR export:
        // half AL + half work), and is not blocked, not rewritten to WIC_DUTY, not re-timed.
        var day = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E1");
        Assert.Equal(ShiftTypes.HalfAL, day.ShiftType);
        Assert.Equal("08:00", day.ShiftStart);
        Assert.Equal("13:00", day.ShiftEnd);
        Assert.False(day.IsWicDuty);

        // The WIC row exists on site and is the intersection (08:00-13:00 ∩ 08:00-13:00).
        var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E1");
        Assert.True(wic.IsOnSite);
        Assert.Equal(Helmstedt, wic.SupportLocation);
        Assert.Equal("08:00-13:00", wic.WorkingShift);
    }

    [Fact]
    public async Task HalfAlDay_WorkingPartLongerThanTheCentre_IsCutToTheCentre()
    {
        using var db = NewDb();
        SeedHelmstedt(db);
        // Working part 10:00-18:00, centre open 08:00-13:00 → the WIC row is 10:00-13:00.
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 1, EmployeeId = "E1", ShiftDate = Fri, ShiftType = ShiftTypes.HalfAL,
            ShiftStart = "10:00", ShiftEnd = "18:00",
        });
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", HelmstedtCode, Fri.ToString("yyyy-MM-dd"), null, null),
            db, manualActor: "TEST-OPERATOR");

        var day = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E1");
        Assert.Equal(ShiftTypes.HalfAL, day.ShiftType);
        Assert.Equal("10:00", day.ShiftStart);
        Assert.Equal("18:00", day.ShiftEnd);

        var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E1");
        Assert.True(wic.IsOnSite);
        Assert.Equal("10:00-13:00", wic.WorkingShift);
    }

    [Fact]
    public async Task FullAlDay_IsStillBlocked_AndNoWicRowIsWritten()
    {
        using var db = NewDb();
        SeedHelmstedt(db);
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 1, EmployeeId = "E1", ShiftDate = Fri, ShiftType = ShiftTypes.AnnualLeave,
            ShiftStart = "08:00", ShiftEnd = "17:00",
        });
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", HelmstedtCode, Fri.ToString("yyyy-MM-dd"), null, null),
            db, manualActor: "TEST-OPERATOR");

        var day = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E1");
        Assert.Equal(ShiftTypes.AnnualLeave, day.ShiftType);
        Assert.Empty(await db.WicShiftEntries.ToListAsync());
    }

    [Fact]
    public async Task HalfAlDay_OpenReportsTheCentreCovered()
    {
        using var db = NewDb();
        SeedHelmstedt(db);
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 1, EmployeeId = "E1", ShiftDate = Fri, ShiftType = ShiftTypes.HalfAL,
            ShiftStart = "08:00", ShiftEnd = "13:00", AgentTask = Helmstedt,
        });
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Fri, DayOfWeek = Fri.DayOfWeek.ToString(),
            SupportLocation = Helmstedt, LocationCode = HelmstedtCode,
            WorkingShift = "08:00-13:00", IsOnSite = true, Task = "WIC",
        });
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        var days = await svc.GetOpenAsync(Fri.ToString("yyyy-MM-dd"), 1);

        var helmstedt = days.Single().Locations.Single(l => l.LocationCode == HelmstedtCode);
        Assert.True(helmstedt.IsOpen);
        Assert.Equal(1, helmstedt.EffectiveCoverage);
        Assert.Equal(1, helmstedt.ScheduledCount);
        Assert.Equal(0, helmstedt.AbsentCount);
        Assert.Equal("COVERED", helmstedt.CoverageStatus);
    }

    [Fact]
    public void HalfAlContribution_IsFullWhenThereIsAnOnSiteRow_AndHalfWithout()
    {
        var halfAl = new ShiftEntry { ShiftType = ShiftTypes.HalfAL };

        // Signal 1 (the agent HAS an on-site WIC row): the working part is WIC duty.
        Assert.Equal(1.0, AvailabilityResolver.GetOnSiteWicContribution(false, halfAl));
        // The generic availability score is untouched.
        Assert.Equal(0.5, AvailabilityResolver.GetWicContribution(false, halfAl));
        // Full absence is still 0.0, and so is "no row at all".
        Assert.Equal(0.0, AvailabilityResolver.GetOnSiteWicContribution(true, halfAl));
        Assert.Equal(0.0, AvailabilityResolver.GetOnSiteWicContribution(false, null));
        // A WIC_DUTY row is unaffected.
        Assert.Equal(1.0, AvailabilityResolver.GetOnSiteWicContribution(
            false, new ShiftEntry { ShiftType = ShiftTypes.WicDuty }));
    }

    [Fact]
    public void HalfAlIsNoLongerABlockingAbsenceType()
    {
        Assert.DoesNotContain("HALF_AL", AvailabilityResolver.BlockingAbsenceTypes);
        foreach (var stillBlocking in new[] { "AL", "SL", "UL", "PH", "LPH", "OL", "OFF", "OFF_WEEKEND", "RESIGNED" })
            Assert.Contains(stillBlocking, AvailabilityResolver.BlockingAbsenceTypes);
    }

    // ── B) Every writer: WIC_DUTY → something else stands the on-site row down ──────────

    private static void SeedWicDutyDay(GSDContext db)
    {
        SeedHelmstedt(db);
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 1, EmployeeId = "E1", ShiftDate = Thu, ShiftType = ShiftTypes.WicDuty, IsWicDuty = true,
            ShiftStart = "08:00", ShiftEnd = "17:00", AgentTask = Helmstedt,
        });
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Thu, DayOfWeek = Thu.DayOfWeek.ToString(),
            SupportLocation = Helmstedt, LocationCode = HelmstedtCode,
            WorkingShift = "08:00-17:00", IsOnSite = true, Task = "WIC",
        });
    }

    [Fact]
    public async Task WicDuty_To_BoWorkingDay_StandsDownTheOnSiteWicRow()
    {
        using var db = NewDb();
        SeedWicDutyDay(db);
        await db.SaveChangesAsync();

        // A BO / back-office day is ShiftType=WORKING with RawValue="BO" — exactly what the
        // roster generator writes (RosterService.ApplyGenerated).
        var shift = await db.ShiftEntries.SingleAsync(s => s.Id == 1);
        shift.ShiftType   = ShiftTypes.Working;
        shift.RawValue    = "BO";
        shift.IsWicDuty   = false;
        await db.SaveChangesAsync();

        var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E1");
        Assert.False(wic.IsOnSite);
        // The historical row (and its window) survives — only the coverage signal changes.
        Assert.Equal("08:00-17:00", wic.WorkingShift);
    }

    [Fact]
    public async Task WicDuty_To_Working_ThroughTheShiftPlanCell_StandsDownTheOnSiteWicRow()
    {
        using var db = NewDb();
        SeedWicDutyDay(db);
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.UpdateShiftAsync(1,
            new ShiftUpdateDto(ShiftTypes.Working, null, null, null, null, null), "TEST-OPERATOR");

        Assert.Null(result.Error);
        Assert.Equal(ShiftTypes.Working, (await db.ShiftEntries.SingleAsync(s => s.Id == 1)).ShiftType);
        Assert.False((await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E1")).IsOnSite);
    }

    [Fact]
    public async Task WicDuty_To_SickLeave_ThroughTheLeaveSync_StandsDownTheOnSiteWicRow()
    {
        using var db = NewDb();
        SeedWicDutyDay(db);
        await db.SaveChangesAsync();

        // Bulk RTM "SL" pastes go to POST /api/sickleave, which writes ShiftEntries through
        // ShiftSyncService.SyncSickLeaveAsync — a writer that never touched WIC before.
        await new ShiftSyncService(db).SyncSickLeaveAsync("E1", Thu, Thu, sourceId: 7);

        Assert.Equal(ShiftTypes.SickLeave, (await db.ShiftEntries.SingleAsync(s => s.Id == 1)).ShiftType);
        Assert.False((await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E1")).IsOnSite);
    }

    [Fact]
    public async Task WicDuty_To_Off_ThroughTheAssignRoute_StandsDownTheOnSiteWicRow()
    {
        using var db = NewDb();
        SeedWicDutyDay(db);
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.AssignShiftAsync(
            new AssignShiftDto("E1", Thu.ToString("yyyy-MM-dd"), ShiftTypes.Off, null, null), "TEST-OPERATOR");

        Assert.Null(result.DuplicateError);
        Assert.Equal(ShiftTypes.Off, (await db.ShiftEntries.SingleAsync(s => s.Id == 1)).ShiftType);
        Assert.False((await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E1")).IsOnSite);
    }

    [Fact]
    public async Task WicDuty_To_AnnualLeave_ThroughTheVacationSync_StandsDownTheOnSiteWicRow()
    {
        using var db = NewDb();
        SeedWicDutyDay(db);
        await db.SaveChangesAsync();

        // Bulk RTM "AL" pastes go to POST /api/shifts/assign/manual; the vacation import
        // path writes the same day through ShiftSyncService.SyncVacationAsync.
        await new ShiftSyncService(db).SyncVacationAsync("E1", Thu, Thu, sourceId: 9);

        Assert.Equal(ShiftTypes.AnnualLeave, (await db.ShiftEntries.SingleAsync(s => s.Id == 1)).ShiftType);
        Assert.False((await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E1")).IsOnSite);
    }

    [Fact]
    public async Task WicDuty_IsNotStoodDownWhenTheWriterKeepsWicDuty()
    {
        using var db = NewDb();
        SeedWicDutyDay(db);
        await db.SaveChangesAsync();

        var shift = await db.ShiftEntries.SingleAsync(s => s.Id == 1);
        shift.ShiftStart = "09:00";
        await db.SaveChangesAsync();

        Assert.True((await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E1")).IsOnSite);
    }

    // ── B) Centre A → centre B (Assign Agent) ───────────────────────────────────────────

    [Fact]
    public async Task AssignAgent_CentreA_To_CentreB_MovesTheOnSiteWicRow()
    {
        using var db = NewDb();
        SeedTwoOverlappingCentres(db);
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 1, EmployeeId = "E1", ShiftDate = Thu, ShiftType = ShiftTypes.WicDuty, IsWicDuty = true,
            ShiftStart = "08:00", ShiftEnd = "17:00", AgentTask = "Centre A",
        });
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Thu, DayOfWeek = Thu.DayOfWeek.ToString(),
            SupportLocation = "Centre A", LocationCode = "C-A",
            WorkingShift = "08:00-17:00", IsOnSite = true, Task = "WIC",
        });
        await db.SaveChangesAsync();

        var svc = new WicShiftService(db, new AvailabilityResolver(db));
        await svc.CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", "C-B", Thu.ToString("yyyy-MM-dd"), null, null),
            db, manualActor: "TEST-OPERATOR");

        // A is gone (replaced, not merely stood down — the assignment moved), B is on site.
        Assert.Empty(await db.WicShiftEntries.Where(w => w.SupportLocation == "Centre A").ToListAsync());
        var moved = await db.WicShiftEntries.SingleAsync(w => w.SupportLocation == "Centre B");
        Assert.True(moved.IsOnSite);
        Assert.Equal("08:00-17:00", moved.WorkingShift);

        var day = await db.ShiftEntries.SingleAsync(s => s.Id == 1);
        Assert.Equal(ShiftTypes.WicDuty, day.ShiftType);
        Assert.Equal("Centre B", day.AgentTask);
    }

    // ── The standalone helper ───────────────────────────────────────────────────────────

    [Fact]
    public void LeavesWicDuty_OnlyTrueForTheRealTransition()
    {
        Assert.True(WicAttendanceLink.LeavesWicDuty("WIC_DUTY", "WORKING"));
        Assert.True(WicAttendanceLink.LeavesWicDuty("WIC_DUTY", "HALF_AL"));
        Assert.True(WicAttendanceLink.LeavesWicDuty("wic_duty", "OFF"));
        Assert.False(WicAttendanceLink.LeavesWicDuty("WIC_DUTY", "WIC_DUTY"));
        Assert.False(WicAttendanceLink.LeavesWicDuty("WORKING", "AL"));
        Assert.False(WicAttendanceLink.LeavesWicDuty(null, "AL"));
        Assert.False(WicAttendanceLink.LeavesWicDuty("WORKING", "WIC_DUTY"));
    }

    [Fact]
    public async Task StandDownAsync_SparsTheNamedCentre()
    {
        using var db = NewDb();
        SeedTwoOverlappingCentres(db);
        db.WicShiftEntries.Add(new WicShiftEntry
        { EmployeeId = "E1", ShiftDate = Thu, SupportLocation = "Centre A", IsOnSite = true, WorkingShift = "08:00-12:00" });
        db.WicShiftEntries.Add(new WicShiftEntry
        { EmployeeId = "E1", ShiftDate = Thu, SupportLocation = "Centre B", IsOnSite = true, WorkingShift = "13:00-17:00" });
        await db.SaveChangesAsync();

        var changed = await WicAttendanceLink.StandDownAsync(db, "E1", Thu, keepLocation: "Centre B");
        await db.SaveChangesAsync();

        Assert.Equal(1, changed);
        Assert.False((await db.WicShiftEntries.SingleAsync(w => w.SupportLocation == "Centre A")).IsOnSite);
        Assert.True((await db.WicShiftEntries.SingleAsync(w => w.SupportLocation == "Centre B")).IsOnSite);
    }
}
