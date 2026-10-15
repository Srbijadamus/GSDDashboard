using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// RTM 2026-10-15, requirements 1 and 2 — A ROW THAT LEFT WIC DUTY IS NOT WIC DUTY.
//
// The live defect (9129428, Pfaffenhofen, 2026-11-16): RTM changed a WIC_DUTY cell to a
// WORKING / BO day, but the centre name stayed in ShiftEntries.AgentTask. Every signal-2
// reader matches AgentTask against a centre, so the agent kept being listed as covering the
// centre — WIC cards, coverage gaps, forecast and briefing all saw a duty the Shift Plan had
// already taken away.
//
// Two rules are pinned here:
//   1. a BO row (RawValue="BO") is never WIC duty in any reader, whatever AgentTask says;
//      and a row that AROSE from WIC_DUTY → non-WIC carries no centre name any more, so no
//      reader can match it; and
//   2. the central hook (GSDContext.LinkWicRowsToShiftTypeChanges) clears that stale centre
//      name in the SAME SaveChanges as the type change, whatever writer made it.
public class BoAgentTaskWicTests
{
    private const string PfaffenhofenCode = "DE~85276~Pfaffenhofen~Draht 7";
    private const string Pfaffenhofen     = "Pfaffenhofen";

    // 2026-11-16 is a Monday.
    private static readonly DateOnly Mon = new(2026, 11, 16);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static WicLocation PfaffenhofenLocation() => new()
    {
        Id = 70,
        LocationCode = PfaffenhofenCode,
        LocationCodeLegacy = "DE_Pfaffenhofen",
        DisplayName = Pfaffenhofen,
        City = "Pfaffenhofen",
        Country = "DE",
        IsActive = true,
        MinAgentsRequired = 1,
    };

    private static void SeedCentre(GSDContext db)
    {
        db.WicLocations.Add(PfaffenhofenLocation());
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = PfaffenhofenCode,
            DayOfWeek = (int)Mon.DayOfWeek,
            OpenTime = "08:00",
            CloseTime = "16:00",
        });
    }

    // ── 1. A BO row is never WIC duty, whatever AgentTask still holds ─────────────────────

    [Fact]
    public void IsBackOfficeRawValue_RecognisesBoAndNothingElse()
    {
        Assert.True(AvailabilityResolver.IsBackOfficeRawValue("BO"));
        Assert.True(AvailabilityResolver.IsBackOfficeRawValue("bo"));
        Assert.True(AvailabilityResolver.IsBackOfficeRawValue(" BO "));
        Assert.False(AvailabilityResolver.IsBackOfficeRawValue("08:00 - 17:00"));
        Assert.False(AvailabilityResolver.IsBackOfficeRawValue("OFF"));
        Assert.False(AvailabilityResolver.IsBackOfficeRawValue(null));
        Assert.False(AvailabilityResolver.IsBackOfficeRawValue(""));
    }

    [Fact]
    public void IsWicDutyAt_BoRowWithCentreInAgentTask_IsNotWicDuty()
    {
        // The exact shape the live rows have (9129428, 16.-27.11.2026).
        var row = new ShiftEntry
        {
            EmployeeId = "9129428",
            ShiftDate = Mon,
            ShiftType = ShiftTypes.Working,
            RawValue = "BO",
            ShiftStart = "07:30",
            ShiftEnd = "16:30",
            AgentTask = Pfaffenhofen,
            IsWicDuty = false,
        };

        Assert.False(AvailabilityResolver.IsWicDutyAt(row, PfaffenhofenLocation()));
        Assert.Equal(0.0, AvailabilityResolver.GetWicContributionAt(false, row, PfaffenhofenLocation()));
        Assert.Equal(0.0, AvailabilityResolver.GetOnSiteWicContribution(false, row, PfaffenhofenLocation()));
    }

    [Fact]
    public void IsWicDutyAt_WicDutyRowWithBoRawValue_IsNotWicDuty()
    {
        // The rule is about the row's meaning, not its ShiftType: a row whose RawValue says
        // back office is not WIC duty even if ShiftType still reads WIC_DUTY.
        var row = new ShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Mon, ShiftType = ShiftTypes.WicDuty,
            RawValue = "BO", AgentTask = Pfaffenhofen, IsWicDuty = true,
        };

        Assert.False(AvailabilityResolver.IsWicDutyAt(row, PfaffenhofenLocation()));
    }

    [Fact]
    public void CandidateDutyRows_DropsBoRows_AndKeepsRealCandidates()
    {
        var bo = new ShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Mon, ShiftType = ShiftTypes.Working,
            RawValue = "BO", AgentTask = Pfaffenhofen,
        };
        var flipped = new ShiftEntry
        {
            EmployeeId = "E2", ShiftDate = Mon, ShiftType = ShiftTypes.Working,
            RawValue = null, AgentTask = Pfaffenhofen,
        };
        var badge = new ShiftEntry
        {
            EmployeeId = "E3", ShiftDate = Mon, ShiftType = ShiftTypes.Working,
            RawValue = "08:00 - 16:00", AgentTask = "GSD",
        };

        var candidates = WicPresenceResolver.CandidateDutyRows(new[] { bo, flipped, badge });

        Assert.DoesNotContain(bo, candidates);
        Assert.Contains(flipped, candidates);
        Assert.DoesNotContain(badge, candidates);
    }

    [Fact]
    public void Resolver_BoAgentWithCentreInAgentTask_IsNotCover_NotEvenRosterOnly()
    {
        // Before the fix this produced ROSTER_ONLY cover and a "staffed, check-in not
        // recorded" badge for an agent who is on back office.
        var bo = new ShiftEntry
        {
            EmployeeId = "9129428", ShiftDate = Mon, ShiftType = ShiftTypes.Working,
            RawValue = "BO", ShiftStart = "07:30", ShiftEnd = "16:30",
            AgentTask = Pfaffenhofen,
        };

        var presence = WicPresenceResolver.Resolve(
            PfaffenhofenLocation(), Mon,
            onSiteRows: new List<WicShiftEntry>(),
            shiftByEmpDate: new Dictionary<(string, DateOnly), ShiftEntry> { [("9129428", Mon)] = bo },
            candidateDutyRows: WicPresenceResolver.CandidateDutyRows(new[] { bo }),
            isSick: _ => false);

        Assert.Equal(WicCoverageEvidence.NONE, presence.Evidence);
        Assert.Equal(0, presence.EffectiveCoverage);
        Assert.Equal(0, presence.CheckInNotRecordedCount);
        Assert.Empty(presence.Agents);
    }

    [Fact]
    public async Task Cards_BoAgentWithCentreInAgentTask_IsNotListed_AndTheRealAgentStays()
    {
        using var db = NewDb();
        SeedCentre(db);
        db.Employees.Add(new Employee { EmployeeId = "9129428", FullName = "Bo Agent", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "9135516", FullName = "Wic Agent", IsActive = true });
        // The live 16.11.2026 shape after the data fix (requirement 3): the row that was a
        // BO day is WORKING with no AgentTask, because the stale centre name was cleared.
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 71111, EmployeeId = "9129428", ShiftDate = Mon, ShiftType = ShiftTypes.Working,
            RawValue = null, ShiftStart = "07:30", ShiftEnd = "16:30", AgentTask = null,
        });
        // The agent who really holds the WIC duty that day.
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 58872, EmployeeId = "9135516", ShiftDate = Mon, ShiftType = ShiftTypes.WicDuty,
            IsWicDuty = true, ShiftStart = "08:00", ShiftEnd = "16:00", AgentTask = Pfaffenhofen,
        });
        await db.SaveChangesAsync();

        var cards = await new WicCardsService(db).GetCardsAsync(Mon, "DE");
        var card = cards.Single(c => c.LocationCode == PfaffenhofenCode);

        var ids = card.AssignedAgents.Select(a => a.EmployeeId).ToList();
        Assert.Contains("9135516", ids);
        Assert.DoesNotContain("9129428", ids);
        Assert.Equal("COVERED", card.CoverageStatus);
    }

    [Fact]
    public async Task Cards_BoAgentWhoKeptTheCentreName_IsNotListed_EvenBeforeTheDataFix()
    {
        // The BO rule alone is enough: RawValue="BO" makes the row non-WIC in every reader,
        // with no dependence on AgentTask having been cleaned up yet.
        using var db = NewDb();
        SeedCentre(db);
        db.Employees.Add(new Employee { EmployeeId = "9129428", FullName = "Bo Agent", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 71111, EmployeeId = "9129428", ShiftDate = Mon, ShiftType = ShiftTypes.Working,
            RawValue = "BO", ShiftStart = "07:30", ShiftEnd = "16:30", AgentTask = Pfaffenhofen,
        });
        await db.SaveChangesAsync();

        var cards = await new WicCardsService(db).GetCardsAsync(Mon, "DE");
        var card = cards.Single(c => c.LocationCode == PfaffenhofenCode);

        Assert.DoesNotContain("9129428", card.AssignedAgents.Select(a => a.EmployeeId));
        Assert.NotEqual("COVERED", card.CoverageStatus);
    }

    // ── 2. The central hook clears the stale centre name on the transition ────────────────

    [Theory]
    [InlineData(ShiftTypes.Working, null)]
    [InlineData(ShiftTypes.Working, "BO")]
    [InlineData(ShiftTypes.Off, null)]
    [InlineData(ShiftTypes.AnnualLeave, null)]
    public async Task Hook_WicDutyToNonWic_ClearsAStaleCentreNameInAgentTask(
        string newType, string? newRawValue)
    {
        using var db = NewDb();
        SeedCentre(db);
        db.Employees.Add(new Employee { EmployeeId = "9129428", FullName = "Bo Agent", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 1, EmployeeId = "9129428", ShiftDate = Mon, ShiftType = ShiftTypes.WicDuty,
            IsWicDuty = true, AgentTask = Pfaffenhofen,
        });
        await db.SaveChangesAsync();

        var shift = await db.ShiftEntries.SingleAsync(s => s.Id == 1);
        shift.ShiftType = newType;
        shift.RawValue  = newRawValue;
        shift.IsWicDuty = false;
        await db.SaveChangesAsync();

        Assert.Null((await db.ShiftEntries.SingleAsync(s => s.Id == 1)).AgentTask);
    }

    [Fact]
    public async Task Hook_WicDutyToWorking_KeepsATaskBadge_ItIsNotACentreName()
    {
        using var db = NewDb();
        SeedCentre(db);
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 1, EmployeeId = "E1", ShiftDate = Mon, ShiftType = ShiftTypes.WicDuty,
            IsWicDuty = true, AgentTask = "GSD",
        });
        await db.SaveChangesAsync();

        var shift = await db.ShiftEntries.SingleAsync(s => s.Id == 1);
        shift.ShiftType = ShiftTypes.Working;
        shift.IsWicDuty = false;
        await db.SaveChangesAsync();

        Assert.Equal("GSD", (await db.ShiftEntries.SingleAsync(s => s.Id == 1)).AgentTask);
    }

    [Fact]
    public async Task Hook_WorkingRowIsSavedAgain_DoesNotTouchItsCentreName()
    {
        // No transition happened, so the hook must not act: a WORKING row whose AgentTask
        // names a centre keeps it (that shape is still read as WIC duty with a flipped
        // ShiftType — the Berlin 2026-10-13 defect that must NOT come back).
        using var db = NewDb();
        SeedCentre(db);
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 1, EmployeeId = "9074431", ShiftDate = Mon, ShiftType = ShiftTypes.Working,
            AgentTask = Pfaffenhofen,
        });
        await db.SaveChangesAsync();

        var shift = await db.ShiftEntries.SingleAsync(s => s.Id == 1);
        shift.ShiftStart = "08:00";
        await db.SaveChangesAsync();

        var saved = await db.ShiftEntries.SingleAsync(s => s.Id == 1);
        Assert.Equal(Pfaffenhofen, saved.AgentTask);
        Assert.True(AvailabilityResolver.IsWicDutyAt(saved, PfaffenhofenLocation()));
    }

    [Fact]
    public async Task Hook_IntoWicDuty_KeepsTheCentreName()
    {
        using var db = NewDb();
        SeedCentre(db);
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 1, EmployeeId = "E1", ShiftDate = Mon, ShiftType = ShiftTypes.Working,
            AgentTask = Pfaffenhofen,
        });
        await db.SaveChangesAsync();

        var shift = await db.ShiftEntries.SingleAsync(s => s.Id == 1);
        shift.ShiftType = ShiftTypes.WicDuty;
        shift.IsWicDuty = true;
        await db.SaveChangesAsync();

        Assert.Equal(Pfaffenhofen, (await db.ShiftEntries.SingleAsync(s => s.Id == 1)).AgentTask);
    }

    // ── The helper the hook uses, in isolation ───────────────────────────────────────────

    [Fact]
    public void ClearsWicCentreAgentTask_OnlyForARealCentreName()
    {
        var centres = new[] { PfaffenhofenLocation() };

        Assert.True(WicAttendanceLink.ClearsWicCentreAgentTask(
            new ShiftEntry { AgentTask = Pfaffenhofen }, centres));
        // A long DisplayName is stored truncated to the 20-char column and still matches.
        Assert.True(WicAttendanceLink.ClearsWicCentreAgentTask(
            new ShiftEntry { AgentTask = "Berlin - Brückenstra" },
            new[] { new WicLocation { LocationCode = "DE~10179~Berlin~Brückenstrasse 6", DisplayName = "Berlin - Brückenstrasse" } }));

        Assert.False(WicAttendanceLink.ClearsWicCentreAgentTask(new ShiftEntry { AgentTask = null }, centres));
        Assert.False(WicAttendanceLink.ClearsWicCentreAgentTask(new ShiftEntry { AgentTask = "   " }, centres));
        Assert.False(WicAttendanceLink.ClearsWicCentreAgentTask(new ShiftEntry { AgentTask = "GSD" }, centres));
        Assert.False(WicAttendanceLink.ClearsWicCentreAgentTask(new ShiftEntry { AgentTask = "Voice" }, centres));
        Assert.False(WicAttendanceLink.ClearsWicCentreAgentTask(new ShiftEntry { AgentTask = "Helmstedt" }, centres));
    }

    // ── The gap list must label a BO row BO, never "WIC {centre}" ─────────────────────────

    [Fact]
    public void ComputeTaskLabel_BoRowWithCentreInAgentTask_ReadsBo()
    {
        var bo = new ShiftEntry
        {
            ShiftType = ShiftTypes.Working, RawValue = "BO", AgentTask = Pfaffenhofen,
        };
        Assert.Equal("BO", WicCoverageGapService.ComputeTaskLabel(bo));

        // A genuinely flipped WORKING row still reads as the WIC duty it is.
        var flipped = new ShiftEntry
        {
            ShiftType = ShiftTypes.Working, RawValue = null, AgentTask = Pfaffenhofen,
        };
        Assert.Equal($"WIC {Pfaffenhofen}", WicCoverageGapService.ComputeTaskLabel(flipped));
    }
}
