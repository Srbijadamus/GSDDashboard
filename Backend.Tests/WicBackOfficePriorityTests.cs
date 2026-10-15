using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// OWNER RULE 2026-10-15 — WIC HAS PRIORITY OVER BO.
//
//   An agent with an ACTIVE WIC assignment (MAIN / BACKUP / REGIONAL) whose plan says BO
//   goes to WIC — UNLESS RTM set BO on that day by hand, in which case BO stands. The
//   centre's working hours always apply: WIC presence = the agent's shift intersected with
//   the centre's window (WicWorkingShift).
//
// This is the successor of the (same-day) "a BO row is never WIC duty" rule: that rule was
// right about a BO day a human authored and wrong about the automatically generated one.
public class WicBackOfficePriorityTests
{
    private const string Code = "DE~85276~Pfaffenhofen~Draht 7";
    private const string Name = "Pfaffenhofen";
    private const string BackupCode = "DE~80331~München~Marienplatz 1";
    private const string BackupName = "München";

    // 2026-11-16 is a Monday.
    private static readonly DateOnly Mon = new(2026, 11, 16);
    private static readonly DateOnly Tue = new(2026, 11, 17);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static WicLocation Loc(int id, string code, string name) => new()
    {
        Id = id, LocationCode = code, DisplayName = name, City = name, Country = "DE",
        IsActive = true, MinAgentsRequired = 1,
    };

    private static WicOpeningHour Open(string code, DayOfWeek dow, string from, string to) => new()
    {
        LocationCode = code, DayOfWeek = (int)dow, OpenTime = from, CloseTime = to,
    };

    private static WicOpeningHour Closed(string code, DayOfWeek dow) => new()
    {
        LocationCode = code, DayOfWeek = (int)dow, IsClosed = true,
    };

    private static ShiftEntry PlanBo(string employeeId, DateOnly date, string from = "07:00", string to = "17:00") => new()
    {
        EmployeeId = employeeId, ShiftDate = date, ShiftType = ShiftTypes.Working,
        RawValue = "BO", ShiftStart = from, ShiftEnd = to, IsWicDuty = false,
    };

    /// <summary>One centre open on Monday 08:00-16:00 with one MAIN agent.</summary>
    private static (GSDContext Db, Employee Emp) SeedPlanBo(
        string employeeId = "E1", string fullName = "Agent One", ShiftEntry? row = null)
    {
        var db = NewDb();
        db.WicLocations.Add(Loc(1, Code, Name));
        db.WicOpeningHours.Add(Open(Code, DayOfWeek.Monday, "08:00", "16:00"));
        db.WicAgentAssignments.Add(new WicAgentAssignment
        {
            Id = 1, LocationCode = Code, EmployeeName = fullName, AssignmentType = "MAIN", IsActive = true,
        });
        var emp = new Employee { EmployeeId = employeeId, FullName = fullName, IsActive = true };
        db.Employees.Add(emp);
        db.ShiftEntries.Add(row ?? PlanBo(employeeId, Mon));
        db.SaveChanges();
        return (db, emp);
    }

    private static WicBackOfficePriority.WicAssignmentIndex IndexOf(GSDContext db) =>
        WicBackOfficePriority.BuildIndex(
            db.WicAgentAssignments.ToList(),
            db.WicLocations.ToList(),
            db.Employees.ToList());

    // ── 1. Plan BO + active assignment = WIC, inside the centre's hours ──────────────────

    [Fact]
    public void PlanBo_WithAnActiveMainAssignment_IsWicDuty_InsideTheCentreHours()
    {
        var (db, _) = SeedPlanBo();

        var row = db.ShiftEntries.Single();
        var loc = db.WicLocations.Single();

        Assert.True(WicBackOfficePriority.IsPlanBackOffice(row));
        Assert.False(WicBackOfficePriority.IsManualBackOffice(row));
        Assert.True(AvailabilityResolver.IsWicDutyAt(row, loc, IndexOf(db), db.WicOpeningHours.ToList()));
        Assert.Equal(1.0, AvailabilityResolver.GetWicContributionAt(
            false, row, loc, IndexOf(db), db.WicOpeningHours.ToList()));
    }

    [Fact]
    public void Sync_CreatesTheOnSiteRow_AsShiftIntersectedWithCentreHours()
    {
        // Shift 07:00-17:00, centre 08:00-16:00 -> WorkingShift 08:00-16:00.
        var (db, _) = SeedPlanBo();

        var wic = Assert.Single(db.WicShiftEntries.Where(w => w.EmployeeId == "E1").ToList());
        Assert.True(wic.IsOnSite);
        Assert.Equal(Name, wic.SupportLocation);
        Assert.Equal(Code, wic.LocationCode);
        Assert.Equal("08:00-16:00", wic.WorkingShift);
        Assert.Equal("WIC", wic.Task);
        // The ShiftEntries row itself is never rewritten into WIC_DUTY by the rule.
        var shift = db.ShiftEntries.Single();
        Assert.Equal(ShiftTypes.Working, shift.ShiftType);
        Assert.Equal("BO", shift.RawValue);
    }

    [Fact]
    public void Sync_PartialOverlap_UsesTheIntersection()
    {
        var (db, _) = SeedPlanBo(row: PlanBo("E1", Mon, "12:00", "20:00"));

        var wic = Assert.Single(db.WicShiftEntries.ToList());
        Assert.Equal("12:00-16:00", wic.WorkingShift);
    }

    // ── 2. RTM's own BO day is never WIC ─────────────────────────────────────────────────

    [Fact]
    public void ManualBo_IsNeverWic_AndGetsNoOnSiteRow()
    {
        var manual = PlanBo("E1", Mon);
        manual.ManualChangeBy = "S60028";
        manual.ManualChangeAt = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        var (db, _) = SeedPlanBo(row: manual);
        var loc = db.WicLocations.Single();
        var row = db.ShiftEntries.Single();

        Assert.True(WicBackOfficePriority.IsManualBackOffice(row));
        Assert.False(AvailabilityResolver.IsWicDutyAt(row, loc, IndexOf(db), db.WicOpeningHours.ToList()));
        Assert.Empty(db.WicShiftEntries.ToList());

        var presence = WicPresenceResolver.Resolve(
            loc, Mon, db.WicShiftEntries.ToList(),
            new Dictionary<(string, DateOnly), ShiftEntry> { [("E1", Mon)] = row },
            WicPresenceResolver.CandidateDutyRows(new[] { row }),
            _ => false, IndexOf(db), db.WicOpeningHours.ToList());

        Assert.Equal(WicCoverageEvidence.NONE, presence.Evidence);
        Assert.Empty(presence.Agents);
    }

    // ── 3. Which centre: MAIN, then BACKUP, then REGIONAL; ambiguous never guesses ───────

    [Fact]
    public void MainClosed_BackupOpen_GoesToTheBackupCentre()
    {
        var db = NewDb();
        db.WicLocations.Add(Loc(1, Code, Name));
        db.WicLocations.Add(Loc(2, BackupCode, BackupName));
        db.WicOpeningHours.Add(Closed(Code, DayOfWeek.Monday));
        db.WicOpeningHours.Add(Open(BackupCode, DayOfWeek.Monday, "09:00", "15:00"));
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 1, LocationCode = Code, EmployeeName = "Agent One", AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 2, LocationCode = BackupCode, EmployeeName = "Agent One", AssignmentType = "BACKUP", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent One", IsActive = true });
        db.ShiftEntries.Add(PlanBo("E1", Mon, "08:00", "17:00"));
        db.SaveChanges();

        var wic = Assert.Single(db.WicShiftEntries.ToList());
        Assert.Equal(BackupName, wic.SupportLocation);
        Assert.Equal("09:00-15:00", wic.WorkingShift);
    }

    [Fact]
    public void RegionalOpen_IsUsed_WhenMainAndBackupAreClosed()
    {
        var db = NewDb();
        db.WicLocations.Add(Loc(1, Code, Name));
        db.WicLocations.Add(Loc(2, BackupCode, BackupName));
        db.WicOpeningHours.Add(Open(Code, DayOfWeek.Monday, "08:00", "12:00"));
        db.WicOpeningHours.Add(Closed(BackupCode, DayOfWeek.Monday));
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 1, LocationCode = BackupCode, EmployeeName = "Agent One", AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 2, LocationCode = BackupCode, EmployeeName = "Agent One", AssignmentType = "BACKUP", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 3, LocationCode = Code, EmployeeName = "Agent One", AssignmentType = "REGIONAL", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent One", IsActive = true });
        db.ShiftEntries.Add(PlanBo("E1", Mon, "08:00", "12:00"));
        db.SaveChanges();

        var wic = Assert.Single(db.WicShiftEntries.ToList());
        Assert.Equal(Name, wic.SupportLocation);
        Assert.Equal("08:00-12:00", wic.WorkingShift);
    }

    [Fact]
    public void TwoOpenMainCentres_IsAmbiguous_StaysBackOffice()
    {
        // The generator's no-guess rule is preserved: with two open MAIN centres the plan
        // BO day cannot be resolved to one centre, so it stays a back-office day.
        var db = NewDb();
        db.WicLocations.Add(Loc(1, Code, Name));
        db.WicLocations.Add(Loc(2, BackupCode, BackupName));
        db.WicOpeningHours.Add(Open(Code, DayOfWeek.Monday, "08:00", "12:00"));
        db.WicOpeningHours.Add(Open(BackupCode, DayOfWeek.Monday, "13:00", "17:00"));
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 1, LocationCode = Code, EmployeeName = "Agent One", AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 2, LocationCode = BackupCode, EmployeeName = "Agent One", AssignmentType = "MAIN", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent One", IsActive = true });
        db.ShiftEntries.Add(PlanBo("E1", Mon));
        db.SaveChanges();

        Assert.Empty(db.WicShiftEntries.ToList());
        Assert.Null(WicBackOfficePriority.PriorityCentre(
            IndexOf(db), "E1", Mon, db.WicOpeningHours.ToList()));
    }

    [Fact]
    public void NoOpenAssignedCentre_StaysBackOffice()
    {
        var db = NewDb();
        db.WicLocations.Add(Loc(1, Code, Name));
        db.WicOpeningHours.Add(Closed(Code, DayOfWeek.Monday));
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 1, LocationCode = Code, EmployeeName = "Agent One", AssignmentType = "MAIN", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent One", IsActive = true });
        db.ShiftEntries.Add(PlanBo("E1", Mon));
        db.SaveChanges();

        Assert.Empty(db.WicShiftEntries.ToList());
    }

    [Fact]
    public void InactiveAssignment_DoesNotPromoteABoDay()
    {
        var db = NewDb();
        db.WicLocations.Add(Loc(1, Code, Name));
        db.WicOpeningHours.Add(Open(Code, DayOfWeek.Monday, "08:00", "16:00"));
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 1, LocationCode = Code, EmployeeName = "Agent One", AssignmentType = "MAIN", IsActive = false });
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent One", IsActive = true });
        db.ShiftEntries.Add(PlanBo("E1", Mon));
        db.SaveChanges();

        Assert.Empty(db.WicShiftEntries.ToList());
    }

    // ── 4. Leave types are never converted ──────────────────────────────────────────────

    [Theory]
    [InlineData(ShiftTypes.AnnualLeave)]
    [InlineData(ShiftTypes.SickLeave)]
    [InlineData(ShiftTypes.UnpaidLeave)]
    [InlineData(ShiftTypes.OtherLeave)]
    [InlineData(ShiftTypes.PublicHol)]
    [InlineData(ShiftTypes.Off)]
    [InlineData(ShiftTypes.HalfAL)]
    [InlineData(ShiftTypes.Training)]
    public void LeaveAndTrainingRows_AreNeverTreatedAsAPlanBoDay(string shiftType)
    {
        var row = PlanBo("E1", Mon);
        row.ShiftType = shiftType;

        Assert.False(WicBackOfficePriority.IsBackOfficeDay(row));
        Assert.False(WicBackOfficePriority.IsPlanBackOffice(row));
        Assert.False(WicBackOfficePriority.IsManualBackOffice(row));
    }

    // ── 5. WIC -> BO -> back (automatic writer) ─────────────────────────────────────────

    [Fact]
    public void WicDuty_To_PlanBo_KeepsAnOnSiteRow_AndBackToWicDuty()
    {
        var (db, _) = SeedPlanBo();

        // Turn the day into a real WIC_DUTY duty at the centre first.
        var shift = db.ShiftEntries.Single();
        shift.ShiftType = ShiftTypes.WicDuty;
        shift.IsWicDuty = true;
        shift.AgentTask = Name;
        db.SaveChanges();
        Assert.Single(db.WicShiftEntries.Where(w => w.IsOnSite).ToList());

        // Automatic writer turns it into a plan BO day (RawValue="BO", no manual marker):
        // WIC keeps priority, so the on-site row survives — cut to the centre window.
        shift.ShiftType = ShiftTypes.Working;
        shift.IsWicDuty = false;
        shift.RawValue = "BO";
        shift.AgentTask = null;
        db.SaveChanges();

        var wic = Assert.Single(db.WicShiftEntries.ToList());
        Assert.True(wic.IsOnSite);
        Assert.Equal(Name, wic.SupportLocation);
        Assert.Equal("08:00-16:00", wic.WorkingShift);

        // Back to WIC_DUTY: still one on-site row, still the same centre.
        shift.ShiftType = ShiftTypes.WicDuty;
        shift.IsWicDuty = true;
        shift.AgentTask = Name;
        db.SaveChanges();
        Assert.Single(db.WicShiftEntries.Where(w => w.IsOnSite).ToList());
    }

    // ── 6. A -> B -> A temporary move ───────────────────────────────────────────────────

    [Fact]
    public void TemporaryMove_CentreA_To_CentreB_AndBack_KeepsTheBoRuleIntact()
    {
        var db = NewDb();
        db.WicLocations.Add(Loc(1, Code, Name));
        db.WicLocations.Add(Loc(2, BackupCode, BackupName));
        db.WicOpeningHours.Add(Open(Code, DayOfWeek.Monday, "08:00", "16:00"));
        db.WicOpeningHours.Add(Open(BackupCode, DayOfWeek.Monday, "09:00", "15:00"));
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 1, LocationCode = Code, EmployeeName = "Agent One", AssignmentType = "MAIN", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Agent One", IsActive = true });

        // Home centre A, plan BO day -> WIC at A.
        var row = PlanBo("E1", Mon, "08:00", "16:00");
        db.ShiftEntries.Add(row);
        db.SaveChanges();
        Assert.Equal(Name, Assert.Single(db.WicShiftEntries.ToList()).SupportLocation);

        // Temporary help at B (manual): the WIC row moves to B, the centre trace survives.
        row.ShiftType = ShiftTypes.WicDuty;
        row.IsWicDuty = true;
        row.AgentTask = BackupName;
        db.WicShiftEntries.RemoveRange(db.WicShiftEntries.ToList());
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Mon, DayOfWeek = Mon.DayOfWeek.ToString(),
            SupportLocation = BackupName, LocationCode = BackupCode,
            WorkingShift = "09:00-15:00", IsOnSite = true, Task = "WIC",
        });
        db.SaveChanges();
        Assert.Equal(BackupName, Assert.Single(db.WicShiftEntries.Where(w => w.IsOnSite).ToList()).SupportLocation);

        // Back to A as a plan BO day: the rule puts him at his MAIN centre again.
        db.WicShiftEntries.RemoveRange(db.WicShiftEntries.ToList());
        row.ShiftType = ShiftTypes.Working;
        row.IsWicDuty = false;
        row.RawValue = "BO";
        row.AgentTask = null;
        db.SaveChanges();

        var back = Assert.Single(db.WicShiftEntries.ToList());
        Assert.Equal(Name, back.SupportLocation);
        Assert.Equal("08:00-16:00", back.WorkingShift);
        Assert.True(back.IsOnSite);
    }

    // ── 7. The readers agree: cards, gaps and forecast ───────────────────────────────────

    [Fact]
    public async Task Cards_PlanBoAgent_IsCovered_AndListed()
    {
        var (db, _) = SeedPlanBo();

        var card = Assert.Single(await new WicCardsService(db).GetCardsAsync(Mon, "DE"));

        Assert.Equal("COVERED", card.CoverageStatus);
        var agent = Assert.Single(card.AssignedAgents);
        Assert.Equal("E1", agent.EmployeeId);
    }

    [Fact]
    public async Task Forecast_PlanBoAgent_CoversTheDay()
    {
        var (db, _) = SeedPlanBo();

        var forecast = await new ForecastService(db).GetForecastAsync(1, null, Mon);
        var loc = forecast.Locations.Single(l => l.LocationCode == Code);
        var day = loc.Forecast.Single(d => d.Date == Mon.ToString("yyyy-MM-dd"));

        Assert.Equal("COVERED", day.Status);
        Assert.Equal(1, day.EffectiveCoverage);
    }

    [Fact]
    public async Task CoverageGaps_PlanBoAgent_LeavesNoGap()
    {
        var (db, _) = SeedPlanBo();
        // A gap needs a date inside the endpoint's window, so use today's Monday-matching
        // fixture only when the window can reach it; instead grade the day directly through
        // the shared covered-dates helper, which is what the endpoint uses.
        var onSite = db.WicShiftEntries.Where(w => w.IsOnSite).ToList();
        var duty = WicPresenceResolver.CandidateDutyRows(db.ShiftEntries.ToList());
        var covered = WicCoverageGapService.ComputeCoveredDates(
            onSite, duty, db.WicLocations.Single(),
            db.ShiftEntries.Where(s => onSite.Select(w => w.EmployeeId).Contains(s.EmployeeId)).ToList(),
            IndexOf(db), db.WicOpeningHours.ToList());

        Assert.Contains(Mon, covered);
    }

    // ── 8. The Binod scenario: a manual day does not put the agent on the centre ────────

    [Fact]
    public async Task Cards_Pfaffenhofen_ManualBinodRows_LeaveOnlyTheRealWicAgent()
    {
        var db = NewDb();
        db.WicLocations.Add(Loc(70, Code, Name));
        db.WicOpeningHours.Add(Open(Code, DayOfWeek.Monday, "08:00", "16:00"));
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 187, LocationCode = Code, EmployeeName = "Binod Dutta", AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { Id = 188, LocationCode = Code, EmployeeName = "Wic Agent", AssignmentType = "MAIN", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "9129428", FullName = "Binod Dutta", IsActive = true });
        db.Employees.Add(new Employee { EmployeeId = "9135516", FullName = "Wic Agent", IsActive = true });

        // Binod's November rows: WORKING, no BO marker, manual, centre name already cleared.
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 71111, EmployeeId = "9129428", ShiftDate = Mon, ShiftType = ShiftTypes.Working,
            RawValue = null, ShiftStart = "08:00", ShiftEnd = "17:00", AgentTask = null,
            ManualChangeBy = "S60028", ManualChangeAt = new DateTime(2026, 10, 15, 11, 0, 0, DateTimeKind.Utc),
        });
        db.ShiftEntries.Add(new ShiftEntry
        {
            Id = 58872, EmployeeId = "9135516", ShiftDate = Mon, ShiftType = ShiftTypes.WicDuty,
            IsWicDuty = true, ShiftStart = "08:00", ShiftEnd = "16:00", AgentTask = Name,
        });
        db.SaveChanges();

        var card = Assert.Single(await new WicCardsService(db).GetCardsAsync(Mon, "DE"));
        var ids = card.AssignedAgents.Select(a => a.EmployeeId).ToList();

        Assert.Contains("9135516", ids);
        Assert.DoesNotContain("9129428", ids);
        Assert.Equal("COVERED", card.CoverageStatus);
    }
}
