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
// who had already left, and the Shift Plan could still show an agent whose exit day was
// inside the window.
//
// This file pins the ONE shared rule (SubstituteEligibility.HasLeft, the rule the Shift
// Plan / Overview / coverage gaps / Find Substitute already use) on the three surfaces
// this task changes:
//   * /api/wic/assign-candidates     — a leaver is not a candidate at all,
//   * /api/employees?active=true&date= — the same rule for the Assign-Agent picker,
//   * GET /api/shifts (Shift Plan)    — a leaver is not a plan row,
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

    private static ShiftFilterParams Window(string from, string to) =>
        new(from, to, null, null, null, null);

    private static async Task<List<string>> PlanIds(GSDContext db, string from, string to)
    {
        var rows = await new ShiftService(db).GetShiftsAsync(Window(from, to));
        return rows.Select(r => r.EmployeeId).ToList();
    }

    [Fact]
    public async Task ShiftPlan_InactiveEmployee_AndLeaverInsideTheWindow_AreNotPlanRows()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("GONE1", "Left In Window"));
        db.Employees.Add(Emp("GONE2", "Inactive Row", active: false));
        db.Employees.Add(Emp("OK1", "Still Here"));
        db.ShiftEntries.Add(Resigned("GONE1", Date.AddDays(3)));
        await db.SaveChangesAsync();

        var ids = await PlanIds(db, Date.ToString("yyyy-MM-dd"), Date.AddDays(6).ToString("yyyy-MM-dd"));

        Assert.Contains("OK1", ids);
        Assert.DoesNotContain("GONE1", ids);
        Assert.DoesNotContain("GONE2", ids);
    }

    [Fact]
    public async Task ShiftPlan_FutureExitDate_LeavesTheAgentInThePlanUntilItPasses()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("FUT1", "Leaves Later"));
        db.ShiftEntries.Add(Resigned("FUT1", Date.AddDays(10)));
        await db.SaveChangesAsync();

        // Window ends before the exit date: the agent is still planned (his real rows matter).
        Assert.Contains("FUT1", await PlanIds(db, Date.ToString("yyyy-MM-dd"), Date.AddDays(6).ToString("yyyy-MM-dd")));
        // Window reaches the exit date: he is no longer a plan row.
        Assert.DoesNotContain("FUT1", await PlanIds(db, Date.ToString("yyyy-MM-dd"), Date.AddDays(10).ToString("yyyy-MM-dd")));
    }

    [Fact]
    public async Task ShiftPlan_OnDemandEmployee_KeepsHisPlace()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("OD1", "On Demand", onDemand: true));
        await db.SaveChangesAsync();

        Assert.Contains("OD1", await PlanIds(db, Date.ToString("yyyy-MM-dd"), Date.AddDays(6).ToString("yyyy-MM-dd")));
    }
}
