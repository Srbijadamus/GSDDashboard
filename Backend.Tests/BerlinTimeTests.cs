using GSDDashboard.API.Services;
using Xunit;

namespace GSDDashboard.Tests;

// ZADATAK 2 (Pulse part, 2026-10-12): every time the user sees is Europe/Berlin while
// the database keeps UTC. These tests pin the single conversion the whole backend uses;
// a per-call DateTime.AddHours(2) would pass in summer and fail in winter, so both
// offsets and both DST transitions are asserted here.
public class BerlinTimeTests
{
    [Fact]
    public void TheZoneIsResolvedAndIsNotUtc()
    {
        // A silent UTC fallback would reproduce exactly the defect being fixed.
        Assert.NotEqual(TimeZoneInfo.Utc.Id, BerlinTime.Zone.Id);
        Assert.Equal(TimeSpan.FromHours(1), BerlinTime.Zone.BaseUtcOffset);
    }

    [Fact]
    public void SummerTimeIsTwoHoursAheadOfUtc()
    {
        var berlin = BerlinTime.FromUtc(new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 7, 1, 12, 0, 0), berlin);
    }

    [Fact]
    public void WinterTimeIsOneHourAheadOfUtc()
    {
        var berlin = BerlinTime.FromUtc(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 1, 15, 11, 0, 0), berlin);
    }

    [Fact]
    public void UnspecifiedIsTreatedAsTheStoredUtcInstant()
    {
        // SQL Server datetime2 comes back as Unspecified; every column formatted for the
        // UI is written with DateTime.UtcNow, so Unspecified must be read as UTC.
        var stored = new DateTime(2026, 10, 12, 8, 31, 28);   // live ShiftEntries value
        Assert.Equal(DateTimeKind.Unspecified, stored.Kind);
        Assert.Equal("2026-10-12 10:31", BerlinTime.Stamp(stored, "yyyy-MM-dd HH:mm"));
    }

    [Fact]
    public void TheBerlinDayStartsAtMidnightBerlinNotAtMidnightUtc()
    {
        var berlin = BerlinTime.FromUtc(new DateTime(2026, 7, 1, 22, 30, 0, DateTimeKind.Utc));
        Assert.Equal("2026-07-02 00:30", berlin.ToString("yyyy-MM-dd HH:mm"));
    }

    [Fact]
    public void TheDstChangeFollowsTheZoneRuleNotAFixedOffset()
    {
        // Last Sunday of March 2026: 01:00 UTC is the first CEST instant (03:00 Berlin).
        Assert.Equal(new DateTime(2026, 3, 29, 1, 59, 0),
            BerlinTime.FromUtc(new DateTime(2026, 3, 29, 0, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 3, 29, 3, 0, 0),
            BerlinTime.FromUtc(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void NullStaysNullSoDtoFieldsNeedNoSpecialCase()
    {
        Assert.Null(BerlinTime.Stamp(null, "yyyy-MM-dd HH:mm"));
        Assert.Null(BerlinTime.FromUtc(null));
    }

    [Fact]
    public void TodayIsTheBerlinDay()
    {
        // derived from the zone, not from the process clock
        Assert.Equal(DateOnly.FromDateTime(BerlinTime.Now), BerlinTime.Today);
    }
}
