using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// Reproduces the Hamburg/Augsburg/Essen-BP1 false-positive: a RosterBatch write left
// ShiftEntries.ShiftType=WIC_DUTY with no paired WicShiftEntries on-site row.
// GetForecastAsync must use the same WicCoverageGapService.ComputeCoveredDates fallback
// coverage-gaps uses, so the two never disagree about "covered" again.
public class ForecastServiceTests
{
    private static GSDContext NewDb()
    {
        var options = new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GSDContext(options);
    }

    [Fact]
    public async Task Forecast_UsesSharedFallback_WhenOnSiteRowMissingButShiftEntryIsWicDuty()
    {
        using var db = NewDb();
        var monday = new DateOnly(2026, 9, 21); // Monday

        db.WicLocations.Add(new WicLocation { LocationCode = "FCLOC", DisplayName = "Forecast Centre", IsActive = true });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "FCLOC", DayOfWeek = 1, OpenTime = "09:00", CloseTime = "17:00" });
        db.Employees.Add(new Employee { EmployeeId = "FC1", FullName = "Forecast Agent", IsActive = true });
        // ShiftEntries says WIC_DUTY at this centre — but deliberately NO WicShiftEntries
        // on-site row at all, reproducing the exact bug (RosterBatch 168/153 pattern).
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "FC1", ShiftDate = monday,
            ShiftType = ShiftTypes.WicDuty, AgentTask = "Forecast Centre", IsWicDuty = true,
        });
        await db.SaveChangesAsync();

        var result = await new ForecastService(db).GetForecastAsync(1, "FCLOC", monday);

        var day = Assert.Single(Assert.Single(result.Locations).Forecast);
        Assert.Equal("COVERED", day.Status);
        Assert.True(day.EffectiveCoverage >= 1, "fallback must count the WIC_DUTY agent as coverage");
        Assert.False(day.IsAtRisk);
    }
}
