using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// Regression suite for the phantom "UNSTAFFED" day at Berlin - Brückenstrasse on
// 2026-10-13 (reported 2026-10-01). Erik Goecks (MAIN) had ShiftEntries #56615:
// ShiftType=WORKING, RawValue=null, ShiftStart/End 08:00-17:00 and
// AgentTask='Berlin - Brückenstra' — the centre's 23-char DisplayName truncated to the
// NVARCHAR(20) column. Written by RosterBatch 155 (CreatedByKid=SYSTEM_WICFIX,
// 2026-09-30 06:09, overwrite mode), whose other 14 Tuesdays at that centre are all
// WIC_DUTY. Every reader gated its ShiftEntries cover signal on ShiftType=WIC_DUTY, so
// one wrong field made the day read as uncovered while the UI showed the centre name in
// the task cell.
//
// Two things are pinned here, and they pull in opposite directions on purpose:
//   - a centre name in AgentTask means WIC duty even when ShiftType reads WORKING; and
//   - an ABSENCE row that still carries a stale centre name must never read as cover
//     (a leave import overwrites ShiftType and leaves AgentTask behind; 14 AL rows are
//     still tagged "Hamburg"). Losing that second half would hide real gaps.
public class WicDutyRecognitionTests
{
    // The real row from WicLocations Id 46: DisplayName is 23 chars, so the AgentTask
    // column holds only its first 20.
    private const string BerlinDisplayName = "Berlin - Brückenstrasse";
    private const string BerlinTruncatedTask = "Berlin - Brückenstra";
    private const string BerlinCode = "DE~10179~Berlin~Brückenstrasse 6";

    private static WicLocation Berlin() => new()
    {
        Id = 46,
        LocationCode = BerlinCode,
        LocationCodeLegacy = "DE_Berlin_Kopenick",
        DisplayName = BerlinDisplayName,
        City = "Berlin",
        MinAgentsRequired = 1,
        IsActive = true,
    };

    private static GSDContext NewDb()
    {
        var options = new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GSDContext(options);
    }

    // ── WicLocationMatcher.MatchesAgentTask ──────────────────────────────────────

    [Fact]
    public void MatchesAgentTask_TruncatedPrefix_MatchesLongerDisplayName()
    {
        // The location-name mismatch case: 20 stored chars vs a 23-char DisplayName.
        Assert.Equal(20, BerlinTruncatedTask.Length);
        Assert.Equal(23, BerlinDisplayName.Length);
        Assert.True(WicLocationMatcher.MatchesAgentTask(BerlinTruncatedTask, Berlin()));
    }

    [Fact]
    public void MatchesAgentTask_ExactDisplayName_Matches() =>
        Assert.True(WicLocationMatcher.MatchesAgentTask(BerlinDisplayName, Berlin()));

    [Fact]
    public void MatchesAgentTask_LocationCodeOrLegacyCode_Matches()
    {
        Assert.True(WicLocationMatcher.MatchesAgentTask(BerlinCode, Berlin()));
        Assert.True(WicLocationMatcher.MatchesAgentTask("DE_Berlin_Kopenick", Berlin()));
    }

    [Fact]
    public void MatchesAgentTask_ShortValue_IsNeverTreatedAsAPrefix()
    {
        // Below the column limit the value was not truncated, so a prefix match would be
        // a guess. This is what keeps a short value from matching a same-initial centre.
        var loc = new WicLocation { LocationCode = "DE_X", DisplayName = "Berlin - Gaußstr." };
        Assert.False(WicLocationMatcher.MatchesAgentTask("Berlin", loc));
        Assert.False(WicLocationMatcher.MatchesAgentTask("B", loc));
    }

    [Fact]
    public void MatchesAgentTask_OtherCentreInSameCity_DoesNotMatch()
    {
        // Erik is MAIN at both Berlin centres; Monday's Gaußstr. duty must not satisfy
        // Tuesday's Brückenstrasse requirement.
        Assert.False(WicLocationMatcher.MatchesAgentTask("Berlin - Gaußstr.", Berlin()));
    }

    [Fact]
    public void MatchesAgentTask_DoubleEncodedValue_IsRepairedThenMatched()
    {
        // Windows-1252 mis-decode of the same name, as historical imports stored it.
        Assert.True(WicLocationMatcher.MatchesAgentTask("Berlin - BrÃ¼ckenstrasse", Berlin()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MatchesAgentTask_BlankValue_DoesNotMatch(string? task) =>
        Assert.False(WicLocationMatcher.MatchesAgentTask(task, Berlin()));

    // ── AvailabilityResolver.IsWicDutyAt ─────────────────────────────────────────

    [Fact]
    public void IsWicDutyAt_WorkingRowNamingTheCentre_IsWicDuty()
    {
        // ShiftEntries #56615 exactly: the row the Overview reported UNSTAFFED.
        var row = new ShiftEntry
        {
            EmployeeId = "9074431",
            ShiftDate = new DateOnly(2026, 10, 13),
            ShiftType = ShiftTypes.Working,
            RawValue = null,
            ShiftStart = "08:00",
            ShiftEnd = "17:00",
            AgentTask = BerlinTruncatedTask,
            IsWicDuty = false,
        };
        Assert.True(AvailabilityResolver.IsWicDutyAt(row, Berlin()));
        Assert.Equal(1.0, AvailabilityResolver.GetWicContributionAt(false, row, Berlin()));
    }

    [Fact]
    public void IsWicDutyAt_AbsenceRowWithStaleCentreName_IsNotWicDuty()
    {
        // The mirror case that must NOT flip: a vacation import overwrote ShiftType and
        // left the old AgentTask in place. Reading this as cover would have hidden the
        // Hamburg AL block that the 2026-09-30 session had to fill.
        var hamburg = new WicLocation { LocationCode = "DE_Hamburg", DisplayName = "Hamburg" };
        var awayTypes = new[]
        {
            ShiftTypes.AnnualLeave, ShiftTypes.SickLeave, ShiftTypes.UnpaidLeave,
            ShiftTypes.OtherLeave, ShiftTypes.PublicHol, ShiftTypes.LocalPH,
            ShiftTypes.Resigned, ShiftTypes.Off, ShiftTypes.OffWeekend,
            ShiftTypes.CompDay, ShiftTypes.Training,
        };
        foreach (var away in awayTypes)
        {
            var row = new ShiftEntry
            {
                EmployeeId = "E1",
                ShiftDate = new DateOnly(2026, 10, 14),
                ShiftType = away,
                AgentTask = "Hamburg",
            };
            Assert.False(AvailabilityResolver.IsWicDutyAt(row, hamburg), away + " must not count as WIC duty");
            Assert.Equal(0.0, AvailabilityResolver.GetWicContributionAt(false, row, hamburg));
        }
    }

    [Fact]
    public void IsWicDutyAt_HalfAl_KeepsItsHalfContribution()
    {
        var hamburg = new WicLocation { LocationCode = "DE_Hamburg", DisplayName = "Hamburg" };
        var row = new ShiftEntry
        {
            EmployeeId = "E1",
            ShiftDate = new DateOnly(2026, 10, 14),
            ShiftType = ShiftTypes.HalfAL,
            AgentTask = "Hamburg",
        };
        Assert.False(AvailabilityResolver.IsWicDutyAt(row, hamburg));
        Assert.Equal(0.5, AvailabilityResolver.GetWicContributionAt(false, row, hamburg));
    }

    [Fact]
    public void IsWicDutyAt_WorkingRowWithATaskBadge_IsNotWicDuty()
    {
        // A raw WORKING row still proves nothing — it only says the agent is clocked in.
        foreach (var badge in new[] { "Voice", "Backlog", "WIC", "VWIC", "SME", "SSP", "Dispatcher", "GSD" })
        {
            var row = new ShiftEntry
            {
                EmployeeId = "E1",
                ShiftDate = new DateOnly(2026, 10, 13),
                ShiftType = ShiftTypes.Working,
                AgentTask = badge,
            };
            Assert.False(AvailabilityResolver.IsWicDutyAt(row, Berlin()), badge + " is not a centre name");
        }
    }

    [Fact]
    public void IsWicDutyAt_SickAgent_NeverContributes()
    {
        var row = new ShiftEntry
        {
            EmployeeId = "9074431",
            ShiftDate = new DateOnly(2026, 10, 13),
            ShiftType = ShiftTypes.Working,
            AgentTask = BerlinTruncatedTask,
        };
        Assert.Equal(0.0, AvailabilityResolver.GetWicContributionAt(true, row, Berlin()));
    }

    [Fact]
    public void IsWicDutyAt_NoRow_IsNotWicDuty() =>
        Assert.False(AvailabilityResolver.IsWicDutyAt(null, Berlin()));

    // ── WicCoverageGapService.ComputeCoveredDates (gap list / banner seam) ───────

    [Fact]
    public void ComputeCoveredDates_FlippedShiftTypeRow_CountsAsCovered()
    {
        var date = new DateOnly(2026, 10, 13);
        var covered = WicCoverageGapService.ComputeCoveredDates(
            onSiteEntries: new List<WicShiftEntry>(),   // Erik has no on-site row that day
            wicDutyEntries: new List<ShiftEntry>
            {
                new()
                {
                    EmployeeId = "9074431", ShiftDate = date,
                    ShiftType = ShiftTypes.Working, AgentTask = BerlinTruncatedTask,
                },
            },
            loc: Berlin(),
            onSiteEmployeeShiftEntries: new List<ShiftEntry>());

        Assert.Contains(date, covered);
    }

    [Fact]
    public void ComputeCoveredDates_AbsenceRowWithStaleCentreName_StaysUncovered()
    {
        var date = new DateOnly(2026, 10, 13);
        var covered = WicCoverageGapService.ComputeCoveredDates(
            onSiteEntries: new List<WicShiftEntry>(),
            wicDutyEntries: new List<ShiftEntry>
            {
                new()
                {
                    EmployeeId = "9074431", ShiftDate = date,
                    ShiftType = ShiftTypes.AnnualLeave, AgentTask = BerlinTruncatedTask,
                },
            },
            loc: Berlin(),
            onSiteEmployeeShiftEntries: new List<ShiftEntry>());

        Assert.DoesNotContain(date, covered);
    }

    // ── ForecastService (Coverage Risk strip / KPI seam) ─────────────────────────

    [Fact]
    public async Task Forecast_FlippedShiftTypeRow_IsCounted_NotUncovered()
    {
        using var db = NewDb();
        var date = new DateOnly(2026, 10, 13); // Tuesday — the centre's only open day

        db.WicLocations.Add(Berlin());
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = BerlinCode,
            DayOfWeek = 2,
            OpenTime = "09:00",
            CloseTime = "17:00",
        });
        db.Employees.Add(new Employee { EmployeeId = "9074431", FullName = "Erik Goecks", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "9074431",
            ShiftDate = date,
            ShiftType = ShiftTypes.Working,
            RawValue = null,
            ShiftStart = "08:00",
            ShiftEnd = "17:00",
            AgentTask = BerlinTruncatedTask,
            IsWicDuty = false,
        });
        await db.SaveChangesAsync();

        var result = await new ForecastService(db).GetForecastAsync(1, BerlinCode, date);

        var day = Assert.Single(Assert.Single(result.Locations).Forecast);
        Assert.True(day.IsOpen);
        Assert.Equal(1, day.EffectiveCoverage);
        Assert.Equal("COVERED", day.Status);
        Assert.False(day.IsAtRisk);
    }

    [Fact]
    public async Task Forecast_OnSiteAgentWhoseShiftTypeWasFlipped_CountsAsPresent()
    {
        // Merlin Voss / Helmstedt 2026-10-01: the same flipped row shape PLUS a
        // WicShiftEntries IsOnSite=1 row at that centre — independent proof the shape
        // means "on duty here". He used to be discarded as 0.0 despite being on site.
        using var db = NewDb();
        var date = new DateOnly(2026, 10, 1); // Thursday
        const string code = "DE~38350~Helmstedt~Schillerstr. 3";

        db.WicLocations.Add(new WicLocation
        {
            LocationCode = code,
            DisplayName = "Helmstedt",
            City = "Helmstedt",
            MinAgentsRequired = 1,
            IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = code,
            DayOfWeek = 4,
            OpenTime = "09:00",
            CloseTime = "17:00",
        });
        db.Employees.Add(new Employee { EmployeeId = "MV1", FullName = "Merlin Voss", IsActive = true });
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "MV1",
            ShiftDate = date,
            SupportLocation = "Helmstedt",
            IsOnSite = true,
            Task = "WIC",
        });
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "MV1",
            ShiftDate = date,
            ShiftType = ShiftTypes.Working,
            AgentTask = "Helmstedt",
            ShiftStart = "08:00",
            ShiftEnd = "17:00",
        });
        await db.SaveChangesAsync();

        var result = await new ForecastService(db).GetForecastAsync(1, code, date);

        var day = Assert.Single(Assert.Single(result.Locations).Forecast);
        Assert.Equal(1, day.ScheduledCount);
        Assert.Equal(1, day.EffectiveCoverage); // was 0 — on-site agent silently discarded
        Assert.Equal("COVERED", day.Status);
    }
}
