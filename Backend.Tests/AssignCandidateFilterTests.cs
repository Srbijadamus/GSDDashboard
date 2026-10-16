using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Employees;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// The 2026-10-16 Assign defect: the Assign picker (AssignAgentModal → /api/employees,
// and the UNCOVERED / coverage-gap row → /api/wic/assign-candidates) still offered agents
// who had already left.
//
// This file pins the ONE shared rule (SubstituteEligibility.HasLeft, the rule the
// Overview / coverage gaps / Find Substitute use) on the surfaces this task changes:
//   * /api/wic/assign-candidates     — a leaver is not a candidate at all,
//   * /api/employees?active=true&date= — the same rule for the Assign-Agent picker,
//   * GET /api/shifts (Shift Plan)    — the SAME rule, but asked about the WINDOW's FIRST
//     day, not its last: only an agent who had already left BEFORE the window opens is
//     dropped. An exit date INSIDE the window keeps him planned, because the days before
//     it are real work and his RESIGNED rows from the exit day onwards still show.
// plus the two deliberate non-changes:
//   * a FUTURE exit date keeps the agent offerable up to the day before it,
//   * an IsOnDemand agent is never dropped by this rule.
public class AssignCandidateFilterTests
{
    private const string Code = "DE~99999~Leavetown~Weg 1";
    private const string Name = "Leavetown";
    private static readonly DateOnly Date = DateOnly.FromDateTime(DateTime.Today);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ShiftEntry Resigned(string emp, DateOnly date) =>
        new() { EmployeeId = emp, ShiftDate = date, ShiftType = SubstituteEligibility.Resigned };

    private static Employee Emp(string id, string name, bool active = true, bool onDemand = false) =>
        new()
        {
            EmployeeId = id, FirstName = name, LastName = "", FullName = name,
            Engagement = "Full Time", PrimaryRole = "WIC", TeamLeadName = "TL One",
            IsActive = active, IsOnDemand = onDemand, SourceSheet = "GSD_WIC",
        };

    private static void SeedCentre(GSDContext db)
    {
        db.WicLocations.Add(new WicLocation { LocationCode = Code, DisplayName = Name, IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment
        { LocationCode = Code, EmployeeName = "Main Agent", AssignmentType = "MAIN", IsActive = true });
    }

    private static async Task<List<string>> CandidateIds(GSDContext db, DateOnly date)
    {
        var result = await new WicAssignCandidatesService(db).GetCandidatesAsync(Code, date);
        Assert.NotNull(result);
        return result!.Candidates.Select(c => c.EmployeeId).ToList();
    }

    // ── /api/wic/assign-candidates ───────────────────────────────────────────────

    [Fact]
    public async Task AssignCandidates_ResignedOrInactive_AreNotCandidatesAtAll()
    {
        using var db = NewDb();
        SeedCentre(db);
        db.Employees.Add(Emp("M1", "Main Agent"));
        db.Employees.Add(Emp("GONE1", "Left Earlier"));
        db.Employees.Add(Emp("GONE2", "Left Today"));
        db.Employees.Add(Emp("GONE3", "Inactive Row", active: false));
        db.Employees.Add(Emp("OK1", "Still Here"));
        db.ShiftEntries.Add(Resigned("GONE1", Date.AddDays(-5)));
        db.ShiftEntries.Add(Resigned("GONE2", Date));
        await db.SaveChangesAsync();

        var ids = await CandidateIds(db, Date);

        Assert.Contains("M1", ids);
        Assert.Contains("OK1", ids);
        Assert.DoesNotContain("GONE1", ids);
        Assert.DoesNotContain("GONE2", ids);
        Assert.DoesNotContain("GONE3", ids);
    }

    [Fact]
    public async Task AssignCandidates_FutureExitDate_IsOfferedBeforeTheExitAndGoneAfterIt()
    {
        using var db = NewDb();
        SeedCentre(db);
        db.Employees.Add(Emp("FUT1", "Leaves Later"));
        // The exit date itself is the RESIGNED row's date (there is no exit-date column).
        db.ShiftEntries.Add(Resigned("FUT1", Date.AddDays(10)));
        await db.SaveChangesAsync();

        // Before the exit date: still a candidate, for every day from the notice to the day before.
        Assert.Contains("FUT1", await CandidateIds(db, Date));
        Assert.Contains("FUT1", await CandidateIds(db, Date.AddDays(9)));

        // On the exit date and after it: not offered any more.
        Assert.DoesNotContain("FUT1", await CandidateIds(db, Date.AddDays(10)));
        Assert.DoesNotContain("FUT1", await CandidateIds(db, Date.AddDays(11)));
    }

    [Fact]
    public async Task AssignCandidates_OnDemandEmployee_IsStillOffered()
    {
        using var db = NewDb();
        SeedCentre(db);
        db.Employees.Add(Emp("OD1", "On Demand", onDemand: true));
        await db.SaveChangesAsync();

        Assert.Contains("OD1", await CandidateIds(db, Date));
    }

    [Fact]
    public async Task AssignCandidates_HealthyActiveEmployee_IsStillOffered()
    {
        // Control: the fix must not empty the picker.
        using var db = NewDb();
        SeedCentre(db);
        db.Employees.Add(Emp("OK1", "Still Here"));
        await db.SaveChangesAsync();

        Assert.Contains("OK1", await CandidateIds(db, Date));
    }

    // ── /api/employees?active=true&date= (the Assign-Agent picker's source) ──────

    [Fact]
    public async Task Employees_WithDate_HideResignedAndInactive_OnlyTheLeaverIsRemoved()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("GONE1", "Left Earlier"));
        db.Employees.Add(Emp("GONE2", "Left Today"));
        db.Employees.Add(Emp("GONE3", "Inactive Row", active: false));
        db.Employees.Add(Emp("OK1", "Still Here"));
        db.ShiftEntries.Add(Resigned("GONE1", Date.AddDays(-5)));
        db.ShiftEntries.Add(Resigned("GONE2", Date));
        await db.SaveChangesAsync();

        var service = new EmployeeService(db);
        var listed = await service.GetEmployeesAsync(null, null, null, null, true, Date);
        var ids = listed.Select(e => e.EmployeeId).ToList();

        Assert.Contains("OK1", ids);
        Assert.DoesNotContain("GONE1", ids);
        Assert.DoesNotContain("GONE2", ids);
        Assert.DoesNotContain("GONE3", ids);
    }

    [Fact]
    public async Task Employees_WithDate_FutureExitDate_IsListedBeforeItAndNotOnOrAfterIt()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("FUT1", "Leaves Later"));
        db.ShiftEntries.Add(Resigned("FUT1", Date.AddDays(10)));
        await db.SaveChangesAsync();

        var service = new EmployeeService(db);

        Assert.Contains("FUT1", (await service.GetEmployeesAsync(null, null, null, null, true, Date)).Select(e => e.EmployeeId));
        Assert.Contains("FUT1", (await service.GetEmployeesAsync(null, null, null, null, true, Date.AddDays(9))).Select(e => e.EmployeeId));
        Assert.DoesNotContain("FUT1", (await service.GetEmployeesAsync(null, null, null, null, true, Date.AddDays(10))).Select(e => e.EmployeeId));
    }

    [Fact]
    public async Task Employees_WithDate_OnDemandEmployee_IsStillListed()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("OD1", "On Demand", onDemand: true));
        await db.SaveChangesAsync();

        var listed = await new EmployeeService(db).GetEmployeesAsync(null, null, null, null, true, Date);
        var row = Assert.Single(listed);
        Assert.Equal("OD1", row.EmployeeId);
        Assert.True(row.IsOnDemand);
    }

    [Fact]
    public async Task Employees_WithoutDate_KeepsTheDirectoryBehaviour()
    {
        // The date is additive: the plain directory (the Employees page) still sees everyone.
        using var db = NewDb();
        db.Employees.Add(Emp("GONE1", "Left Earlier"));
        db.Employees.Add(Emp("OK1", "Still Here"));
        db.ShiftEntries.Add(Resigned("GONE1", Date.AddDays(-5)));
        await db.SaveChangesAsync();

        var ids = (await new EmployeeService(db).GetEmployeesAsync(null, null, null, null, true))
            .Select(e => e.EmployeeId).ToList();

        Assert.Contains("OK1", ids);
        Assert.Contains("GONE1", ids);
    }

    // ── Shift Plan (GET /api/shifts) ─────────────────────────────────────────────
    //
    // 2026-10-16 owner decision: the plan is the roster of the requested WINDOW, so an agent
    // is dropped ONLY when he had already left BEFORE the window's first day (resigned rows
    // are loaded with `ShiftDate < from` and HasLeft is asked about `from`). An exit date
    // INSIDE the window keeps him planned: the days before the exit are real work, and the
    // RESIGNED rows from the exit day onwards keep showing exactly as before.

    private static ShiftFilterParams Window(string from, string to) =>
        new(from, to, null, null, null, null);

    private static Task<List<ShiftRowDto>> PlanRows(GSDContext db, string from, string to) =>
        new ShiftService(db).GetShiftsAsync(Window(from, to));

    private static async Task<List<string>> PlanIds(GSDContext db, string from, string to)
    {
        var rows = await PlanRows(db, from, to);
        return rows.Select(r => r.EmployeeId).ToList();
    }

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    [Fact]
    public async Task ShiftPlan_LeaverInsideTheWindow_StaysWithTheDaysBeforeTheExit()
    {
        // The exit (Date+3) falls inside the Date..Date+6 window: the agent must stay, with
        // his real WORKING rows before the exit and the RESIGNED row on the exit day itself.
        using var db = NewDb();
        db.Employees.Add(Emp("MID1", "Leaves Mid Window"));
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "MID1", ShiftDate = Date.AddDays(1), ShiftType = ShiftTypes.Working });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "MID1", ShiftDate = Date.AddDays(2), ShiftType = ShiftTypes.Working });
        db.ShiftEntries.Add(Resigned("MID1", Date.AddDays(3)));
        await db.SaveChangesAsync();

        var rows = await PlanRows(db, D(Date), D(Date.AddDays(6)));

        Assert.Contains("MID1", rows.Select(r => r.EmployeeId));
        var beforeExit = rows
            .Where(r => r.EmployeeId == "MID1" && r.ShiftType == ShiftTypes.Working)
            .Select(r => r.ShiftDate).OrderBy(d => d).ToList();
        Assert.Equal(new[] { Date.AddDays(1), Date.AddDays(2) }, beforeExit);
        Assert.Contains(rows, r => r.EmployeeId == "MID1"
                                   && r.ShiftType == SubstituteEligibility.Resigned
                                   && r.ShiftDate == Date.AddDays(3));
    }

    [Fact]
    public async Task ShiftPlan_LeaverBeforeTheWindow_IsNotAPlanRow()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("GONE1", "Left Earlier"));
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "GONE1", ShiftDate = Date.AddDays(-1), ShiftType = ShiftTypes.Working });
        db.ShiftEntries.Add(Resigned("GONE1", Date.AddDays(-1)));
        await db.SaveChangesAsync();

        var ids = await PlanIds(db, D(Date), D(Date.AddDays(6)));

        Assert.DoesNotContain("GONE1", ids);
    }

    [Fact]
    public async Task ShiftPlan_ResignedOnTheFirstShownDay_IsStillAPlanRow()
    {
        // "Before the window" is strict: a RESIGNED row ON `from` leaves the agent in the plan.
        using var db = NewDb();
        db.Employees.Add(Emp("TODAY1", "Leaves On First Day"));
        db.ShiftEntries.Add(Resigned("TODAY1", Date));
        await db.SaveChangesAsync();

        var ids = await PlanIds(db, D(Date), D(Date.AddDays(6)));

        Assert.Contains("TODAY1", ids);
    }

    [Fact]
    public async Task ShiftPlan_ActiveEmployeeWithoutExit_IsAPlanRow()
    {
        // Control: the fix must not empty the plan for an ordinary active agent.
        using var db = NewDb();
        db.Employees.Add(Emp("OK1", "Still Here"));
        await db.SaveChangesAsync();

        var ids = await PlanIds(db, D(Date), D(Date.AddDays(6)));

        Assert.Contains("OK1", ids);
    }

    [Fact]
    public async Task ShiftPlan_InactiveEmployee_IsNeverAPlanRow()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("GONE2", "Inactive Row", active: false));
        await db.SaveChangesAsync();

        var ids = await PlanIds(db, D(Date), D(Date.AddDays(6)));

        Assert.DoesNotContain("GONE2", ids);
    }

    [Fact]
    public async Task ShiftPlan_FutureExitDate_KeepsTheAgentUntilAWindowStartsAfterIt()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("FUT1", "Leaves Later"));
        db.ShiftEntries.Add(Resigned("FUT1", Date.AddDays(10)));
        await db.SaveChangesAsync();

        // Window ends before the exit date: the agent is planned (his real rows matter).
        Assert.Contains("FUT1", await PlanIds(db, D(Date), D(Date.AddDays(6))));
        // Window reaches the exit date: still planned, because the exit is INSIDE the window.
        Assert.Contains("FUT1", await PlanIds(db, D(Date), D(Date.AddDays(10))));
        // The first window to START after the exit date: now he had left before it, so he is gone.
        Assert.DoesNotContain("FUT1", await PlanIds(db, D(Date.AddDays(11)), D(Date.AddDays(17))));
    }

    [Fact]
    public async Task ShiftPlan_OnDemandEmployee_KeepsHisPlace()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("OD1", "On Demand", onDemand: true));
        await db.SaveChangesAsync();

        Assert.Contains("OD1", await PlanIds(db, D(Date), D(Date.AddDays(6))));
    }
}
