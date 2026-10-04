using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// RTM principle (2026-10-01): a manual change is a legitimate decision, never an error and
// never a gap. Covers manual exception days, ad-hoc deployments, hand-edited rows and
// manual assignments.
//
// Before this, there was no way to record "this centre is open today as an exception":
// WicOpeningHours is keyed by DayOfWeek and EffectiveFrom means "from this date onward",
// so Berlin - Brückenstrasse read CLOSED on every surface on Thu 2026-10-01. And a
// hand-edited row was not protected — FUNCTIONS.md's overwrite list covers only
// ProtectedAbsenceTypes, which a hand-edited WORKING/BO row is not in.
public class WicManualChangeTests
{
    private const string Code = "DE~10179~Berlin~Brückenstrasse 6";
    private const string Name = "Berlin - Brückenstrasse";
    private static readonly DateOnly Thu = new(2026, 10, 1);   // Thursday
    private static readonly DateOnly Tue = new(2026, 10, 6);   // the centre's normal open day

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static WicLocation Loc() => new()
    {
        Id = 46, LocationCode = Code, DisplayName = Name, City = "Berlin",
        MinAgentsRequired = 1, IsActive = true,
    };

    /// <summary>The real pattern: open Tuesdays only, closed every other weekday.</summary>
    private static List<WicOpeningHour> WeeklyPattern() =>
        Enumerable.Range(0, 7).Select(dow => new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = dow,
            IsClosed = dow != 2,
            OpenTime = dow == 2 ? "09:00" : null,
            CloseTime = dow == 2 ? "17:00" : null,
        }).ToList();

    private static WicOpeningHour ExceptionOpen(DateOnly date) => new()
    {
        LocationCode = Code, DayOfWeek = (int)date.DayOfWeek,
        ExceptionDate = date, IsClosed = false,
        OpenTime = "09:00", CloseTime = "17:00",
        ManualChangeBy = "S60028", ManualChangeAt = new DateTime(2026, 10, 1, 7, 5, 0),
    };

    // ── 1. One hours definition, exception days included ────────────────────────

    [Fact]
    public void WeekdayPattern_SaysClosed_OnThursday()
    {
        var hours = WicHoursResolver.Resolve(WeeklyPattern(), Code, null, (int)Thu.DayOfWeek, Thu);
        Assert.True(hours!.IsClosed);
    }

    [Fact]
    public void ManualExceptionDay_OpensTheCentre_OnThatDateOnly()
    {
        var all = WeeklyPattern();
        all.Add(ExceptionOpen(Thu));

        var thursday = WicHoursResolver.Resolve(all, Code, null, (int)Thu.DayOfWeek, Thu);
        Assert.False(thursday!.IsClosed);
        Assert.Equal("09:00", thursday.OpenTime);
        Assert.Equal("S60028", thursday.ManualChangeBy);

        // The exception must not leak into the following Thursday...
        var nextThursday = Thu.AddDays(7);
        Assert.True(WicHoursResolver.Resolve(all, Code, null, (int)nextThursday.DayOfWeek, nextThursday)!.IsClosed);
        // ...nor disturb the normal Tuesday.
        Assert.False(WicHoursResolver.Resolve(all, Code, null, (int)Tue.DayOfWeek, Tue)!.IsClosed);
    }

    [Fact]
    public void BothResolverOverloads_HonourTheException()
    {
        // Every surface goes through one of these two, so both must agree.
        var all = WeeklyPattern();
        all.Add(ExceptionOpen(Thu));
        Assert.False(WicHoursResolver.Resolve(all, Code, (int)Thu.DayOfWeek, Thu)!.IsClosed);
        Assert.False(WicHoursResolver.Resolve(all, Code, null, (int)Thu.DayOfWeek, Thu)!.IsClosed);
    }

    [Fact]
    public void ManualExceptionCanAlsoCloseANormallyOpenDay()
    {
        var all = WeeklyPattern();
        all.Add(new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = (int)Tue.DayOfWeek, ExceptionDate = Tue,
            IsClosed = true, ManualChangeBy = "S60028",
        });
        Assert.True(WicHoursResolver.Resolve(all, Code, null, (int)Tue.DayOfWeek, Tue)!.IsClosed);
    }

    // ── 5. An exception-open day is open on the real surfaces, not a false gap ───

    [Fact]
    public async Task ExceptionOpenDay_IsOpenInForecast_AndNotAGapWhenStaffed()
    {
        using var db = NewDb();
        db.WicLocations.Add(Loc());
        db.WicOpeningHours.AddRange(WeeklyPattern());
        db.WicOpeningHours.Add(ExceptionOpen(Thu));
        db.Employees.Add(new Employee { EmployeeId = "9074431", FullName = "Erik Goecks", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "9074431", ShiftDate = Thu, ShiftType = ShiftTypes.WicDuty,
            AgentTask = Name[..20], ShiftStart = "09:00", ShiftEnd = "17:00",
            ManualChangeBy = "S60028", ManualChangeAt = new DateTime(2026, 10, 1, 7, 5, 0),
        });
        await db.SaveChangesAsync();

        var day = Assert.Single(Assert.Single(
            (await new ForecastService(db).GetForecastAsync(1, Code, Thu)).Locations).Forecast);
        Assert.True(day.IsOpen);                    // was CLOSED
        Assert.Equal(1, day.EffectiveCoverage);
        Assert.Equal("COVERED", day.Status);
        Assert.False(day.IsAtRisk);
    }

    [Fact]
    public async Task ExceptionOpenDay_WithNobodyAtAll_IsStillARealGap()
    {
        // The feature must keep working: open day, nobody rostered and nobody checked in.
        using var db = NewDb();
        db.WicLocations.Add(Loc());
        db.WicOpeningHours.AddRange(WeeklyPattern());
        db.WicOpeningHours.Add(ExceptionOpen(Thu));
        await db.SaveChangesAsync();

        var day = Assert.Single(Assert.Single(
            (await new ForecastService(db).GetForecastAsync(1, Code, Thu)).Locations).Forecast);
        Assert.True(day.IsOpen);
        Assert.Equal("UNCOVERED", day.Status);
        Assert.True(day.IsAtRisk);
    }

    [Fact]
    public async Task ManualDuty_DoesNotRaiseTheCheckInNotRecordedWarning()
    {
        using var db = NewDb();
        db.WicLocations.Add(Loc());
        db.WicOpeningHours.AddRange(WeeklyPattern());
        db.WicOpeningHours.Add(ExceptionOpen(Thu));
        db.WicAgentAssignments.Add(new WicAgentAssignment
        {
            LocationCode = Code, EmployeeName = "Erik Goecks", AssignmentType = "MAIN", IsActive = true,
        });
        db.Employees.Add(new Employee { EmployeeId = "9074431", FullName = "Erik Goecks", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "9074431", ShiftDate = Thu, ShiftType = ShiftTypes.WicDuty,
            AgentTask = Name[..20], ShiftStart = "09:00", ShiftEnd = "17:00",
            ManualChangeBy = "S60028", ManualChangeAt = new DateTime(2026, 10, 1, 7, 5, 0),
        });
        await db.SaveChangesAsync();

        var card = Assert.Single(await new WicCardsService(db).GetCardsAsync(Thu, null));
        Assert.Equal("COVERED", card.CoverageStatus);
        Assert.Equal(0, card.CheckInNotRecordedCount);   // no data-quality complaint
        Assert.Equal(1, card.ManualCount);               // neutral "Manual" label instead
        var agent = Assert.Single(card.AssignedAgents);
        Assert.True(agent.IsManualChange);
        Assert.Equal("S60028", agent.ManualChangeBy);
        Assert.False(agent.CheckInNotRecorded);
    }

    // ── 4. Protection: the generator must never overwrite a manual row ───────────

    [Fact]
    public async Task RosterOverwrite_NeverReplacesAManualRow()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent E1", IsActive = true });
        db.WicLocations.Add(new WicLocation
        {
            LocationCode = "DE~X~Centre", DisplayName = "Centre", MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = "DE~X~Centre", DayOfWeek = (int)Thu.DayOfWeek,
            OpenTime = "09:00", CloseTime = "17:00",
        });
        // A hand-edited plain working row — NOT one of ProtectedAbsenceTypes, so the only
        // thing standing between it and Overwrite mode is the manual marker.
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Thu, ShiftType = ShiftTypes.Working, RawValue = "BO",
            ShiftStart = "08:00", ShiftEnd = "17:00",
            ManualChangeBy = "S60028", ManualChangeAt = new DateTime(2026, 10, 1, 7, 5, 0),
        });
        await db.SaveChangesAsync();

        var svc = new RosterService(db, new WicShiftService(db, new AvailabilityResolver(db)),
                                    new WicCoverageGapService(db));
        var preview = await svc.PreviewAsync(new RosterRequest(
            "E1", "2026-10-01", "2026-10-01", null, null, null, null, "DE~X~Centre", Overwrite: true));

        Assert.Empty(preview.Replacements);                 // nothing would be rewritten
        Assert.Empty(preview.Rows);
        var collision = Assert.Single(preview.Collisions);  // reported, not silently skipped
        Assert.Equal("2026-10-01", collision.Date);
    }

    [Fact]
    public async Task RosterOverwrite_StillReplacesAnAutomaticRow()
    {
        // The protection must be specific to manual rows, not a blanket block.
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent E1", IsActive = true });
        db.WicLocations.Add(new WicLocation
        {
            LocationCode = "DE~X~Centre", DisplayName = "Centre", MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = "DE~X~Centre", DayOfWeek = (int)Thu.DayOfWeek,
            OpenTime = "09:00", CloseTime = "17:00",
        });
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Thu, ShiftType = ShiftTypes.Working, RawValue = "BO",
            ShiftStart = "08:00", ShiftEnd = "17:00", SourceModule = "Roster",
        });
        await db.SaveChangesAsync();

        var svc = new RosterService(db, new WicShiftService(db, new AvailabilityResolver(db)),
                                    new WicCoverageGapService(db));
        var preview = await svc.PreviewAsync(new RosterRequest(
            "E1", "2026-10-01", "2026-10-01", null, null, null, null, "DE~X~Centre", Overwrite: true));

        var replaced = Assert.Single(preview.Replacements);
        Assert.Equal("2026-10-01", replaced.Date);
        Assert.Equal("WIC", replaced.Kind);
    }
}
