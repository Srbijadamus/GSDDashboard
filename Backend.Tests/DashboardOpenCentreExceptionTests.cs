using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Dashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GSDDashboard.Tests;

// DashboardService.cs:92-97 used to read the open exception row and the open weekday
// pattern side by side under one !IsClosed filter, so a CLOSED exception day fell through
// to the open pattern and the centre still counted as open. These tests pin the canonical
// precedence (the same CoverageEvaluator/WicHoursResolver rule): the manual exception row
// for the DATE first, then the weekday pattern, and only then IsClosed.
public class DashboardOpenCentreExceptionTests
{
    private static readonly DateOnly Mon = new(2026, 10, 12); // Monday

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DashboardService NewSvc(GSDContext db) =>
        new(db, NullLogger<DashboardService>.Instance);

    private static void SeedCentre(GSDContext db, string code, string name) =>
        db.WicLocations.Add(new WicLocation
        {
            LocationCode = code, DisplayName = name, City = name, IsActive = true,
        });

    private static void SeedException(GSDContext db, string code, bool closed,
        string? open = null, string? close = null) =>
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = code,
            DayOfWeek = (int)DayOfWeek.Monday,
            OpenTime = open,
            CloseTime = close,
            IsClosed = closed,
            ExceptionDate = Mon,
            ManualChangeAt = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc),
        });

    // A plain weekday pattern row: no exception involved.
    [Fact]
    public async Task WeekdayPattern_StillCountsTheCentreAsOpen()
    {
        using var db = NewDb();
        SeedCentre(db, "A", "Centre A");
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = "A", DayOfWeek = (int)DayOfWeek.Monday,
            OpenTime = "09:00", CloseTime = "17:00", IsClosed = false,
        });
        await db.SaveChangesAsync();

        var summary = await NewSvc(db).GetSummaryAsync(Mon);

        Assert.Equal(1, summary.WicUnoccupiedCount);
    }

    // THE bug: the pattern says open, a manual exception closes the centre for that one
    // date. The centre must NOT count as open.
    [Fact]
    public async Task ClosedExceptionDay_DoesNotCountAsOpen()
    {
        using var db = NewDb();
        SeedCentre(db, "A", "Centre A");
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = "A", DayOfWeek = (int)DayOfWeek.Monday,
            OpenTime = "09:00", CloseTime = "17:00", IsClosed = false,
        });
        SeedException(db, "A", closed: true);
        await db.SaveChangesAsync();

        var summary = await NewSvc(db).GetSummaryAsync(Mon);

        Assert.Equal(0, summary.WicUnoccupiedCount);
    }

    // The mirror case: the pattern says closed, a manual exception opens the centre for
    // that one date. It must count as open.
    [Fact]
    public async Task OpenExceptionDay_CountsAsOpen()
    {
        using var db = NewDb();
        SeedCentre(db, "A", "Centre A");
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = "A", DayOfWeek = (int)DayOfWeek.Monday, IsClosed = true,
        });
        SeedException(db, "A", closed: false, open: "10:00", close: "14:00");
        await db.SaveChangesAsync();

        var summary = await NewSvc(db).GetSummaryAsync(Mon);

        Assert.Equal(1, summary.WicUnoccupiedCount);
    }

    // An exception on ANOTHER date must not leak into this one (it is not a pattern row).
    [Fact]
    public async Task ExceptionOnAnotherDate_IsIgnored()
    {
        using var db = NewDb();
        SeedCentre(db, "A", "Centre A");
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = "A", DayOfWeek = (int)DayOfWeek.Monday, IsClosed = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = "A", DayOfWeek = (int)DayOfWeek.Monday,
            OpenTime = "10:00", CloseTime = "14:00", IsClosed = false,
            ExceptionDate = Mon.AddDays(1),
            ManualChangeAt = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();

        var summary = await NewSvc(db).GetSummaryAsync(Mon);

        Assert.Equal(0, summary.WicUnoccupiedCount);
    }
}
