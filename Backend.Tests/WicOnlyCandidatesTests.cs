using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GSDDashboard.Tests;

// The Overview v2 "Other options" card and the "Assign manually" dialog offered
// agents who never actually go to WIC (e.g. an SSP backlog agent without a WIC
// assignment). Both surfaces must offer ONLY employees with an active
// WicAgentAssignments row — centre-assigned (MAIN/BACKUP/REGIONAL) first, then
// the other WIC agents. Search in "Assign manually" still finds every active
// employee (frontend behaviour, tested there); this file pins the backend:
//   * /api/wic/substitutes?wicOnly=true  — the "Other options" source,
//   * /api/wic/manual-candidates         — the "Assign manually" default list.
public class WicOnlyCandidatesTests
{
    private const string Code = "DE~100~Testtown~Weg 1";
    private const string OtherCode = "DE~200~Othertown~Weg 2";
    private static readonly DateOnly Date = DateOnly.FromDateTime(DateTime.Today);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Employee Emp(string id, string name, string role = "WIC", bool active = true) =>
        new()
        {
            EmployeeId = id, FirstName = name, LastName = "", FullName = name,
            Engagement = "Full Time", PrimaryRole = role, TeamLeadName = "TL One",
            IsActive = active, SourceSheet = "GSD_WIC",
        };

    // ── /api/wic/manual-candidates ─────────────────────────────────────────────

    [Fact]
    public async Task ManualCandidates_OnlyWicAgents_CentreFirstThenOtherWic()
    {
        using var db = NewDb();
        db.WicLocations.Add(new WicLocation { LocationCode = Code, DisplayName = "Testtown", IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = OtherCode, DisplayName = "Othertown", IsActive = true });

        db.Employees.Add(Emp("M1", "Main Agent"));
        db.Employees.Add(Emp("B1", "Backup Agent"));
        db.Employees.Add(Emp("R1", "Regional Agent"));
        db.Employees.Add(Emp("O1", "Other Wic Agent"));
        db.Employees.Add(Emp("N1", "No Assignment"));

        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = Code, EmployeeName = "Main Agent", AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = Code, EmployeeName = "Backup Agent", AssignmentType = "BACKUP", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = Code, EmployeeName = "Regional Agent", AssignmentType = "REGIONAL", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = OtherCode, EmployeeName = "Other Wic Agent", AssignmentType = "MAIN", IsActive = true });
        await db.SaveChangesAsync();

        var result = await new WicManualCandidatesService(db).GetCandidatesAsync(Code);

        Assert.NotNull(result);
        var ids = result!.Candidates.Select(c => c.EmployeeId).ToList();
        // MAIN → BACKUP → REGIONAL (centre) first, then the other WIC agent. No non-WIC agent.
        Assert.Equal(new[] { "M1", "B1", "R1", "O1" }, ids);
        Assert.Equal("CENTRE", result.Candidates[0].Category);
        Assert.Equal("OTHER_WIC", result.Candidates[3].Category);
    }

    [Fact]
    public async Task ManualCandidates_InactiveAssignmentOrEmployee_AreExcluded()
    {
        using var db = NewDb();
        db.WicLocations.Add(new WicLocation { LocationCode = Code, DisplayName = "Testtown", IsActive = true });

        db.Employees.Add(Emp("M1", "Main Agent"));
        db.Employees.Add(Emp("X1", "Inactive Employee", active: false));
        db.Employees.Add(Emp("Y1", "Inactive Assignment"));

        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = Code, EmployeeName = "Main Agent", AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = Code, EmployeeName = "Inactive Assignment", AssignmentType = "BACKUP", IsActive = false });
        await db.SaveChangesAsync();

        var result = await new WicManualCandidatesService(db).GetCandidatesAsync(Code);

        var ids = result!.Candidates.Select(c => c.EmployeeId).ToList();
        Assert.Equal(new[] { "M1" }, ids);
    }

    // ── /api/wic/substitutes?wicOnly=true ("Other options") ────────────────────

    private static ServiceProvider NewProvider(string name)
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));
        sc.AddDbContext<GSDContext>(o => o.UseInMemoryDatabase(name));
        sc.AddScoped<ReachabilityService>();
        sc.AddScoped<CoverageEvaluator>();
        return sc.BuildServiceProvider();
    }

    private async Task<(SubstitutionService svc, GSDContext db)> SeedSubstitutes(string name)
    {
        var provider = NewProvider(name);
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GSDContext>();

        db.WicLocations.Add(new WicLocation
        {
            LocationCode = Code, DisplayName = "Testtown", City = "Testtown",
            MinAgentsRequired = 1, IsActive = true,
        });
        db.WicLocations.Add(new WicLocation
        {
            LocationCode = OtherCode, DisplayName = "Othertown", City = "Othertown",
            MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = (int)Date.DayOfWeek, OpenTime = "08:00", CloseTime = "17:00",
        });

        // MAIN is the gap owner (no WicShiftEntry -> absent here), BACKUP is a centre agent.
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = Code, EmployeeName = "Main Agent", AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = Code, EmployeeName = "Backup Agent", AssignmentType = "BACKUP", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = OtherCode, EmployeeName = "Other Wic Agent", AssignmentType = "MAIN", IsActive = true });

        db.Employees.Add(Emp("M1", "Main Agent"));
        db.Employees.Add(Emp("B1", "Backup Agent"));
        db.Employees.Add(Emp("O1", "Other Wic Agent"));
        // Non-WIC agents that must disappear under wicOnly:
        db.Employees.Add(Emp("S1", "SSP One", role: "SSP"));
        db.Employees.Add(Emp("V1", "Voice One", role: "Voice"));
        await db.SaveChangesAsync();

        var svc = new SubstitutionService(
            db, new ReachabilityService(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>()),
            new CoverageEvaluator(db), NullLogger<SubstitutionService>.Instance);
        return (svc, db);
    }

    [Fact]
    public async Task Substitutes_WicOnly_RemovesNonWicAgents_KeepsWicAgentsCentreFirst()
    {
        var (svc, _) = await SeedSubstitutes(Guid.NewGuid().ToString());

        var result = await svc.GetSubstitutesAsync(Code, Date.ToString("yyyy-MM-dd"), 1, null, wicOnly: true);

        Assert.NotNull(result);
        var day = Assert.Single(result!.Days);
        var ids = day.Candidates.Select(c => c.EmployeeId).ToList();

        Assert.Contains("B1", ids); // centre BACKUP
        Assert.Contains("M1", ids); // centre MAIN (day-off call-in)
        Assert.Contains("O1", ids); // other WIC agent
        Assert.DoesNotContain("S1", ids); // SSP without a WIC assignment
        Assert.DoesNotContain("V1", ids); // Voice without a WIC assignment

        // Centre-assigned agents sort before the other WIC agent.
        var oIndex = ids.IndexOf("O1");
        Assert.True(ids.IndexOf("B1") < oIndex, "centre BACKUP must precede other-WIC");
        Assert.True(ids.IndexOf("M1") < oIndex, "centre MAIN must precede other-WIC");
    }

    [Fact]
    public async Task Substitutes_Default_StillIncludesNonWicAgents_ForOtherCallers()
    {
        // Control: the wicOnly flag is opt-in; the Find-Substitute drawer and every
        // other caller keep the existing (wide) candidate pool.
        var (svc, _) = await SeedSubstitutes(Guid.NewGuid().ToString());

        var result = await svc.GetSubstitutesAsync(Code, Date.ToString("yyyy-MM-dd"), 1);

        var day = Assert.Single(result!.Days);
        var ids = day.Candidates.Select(c => c.EmployeeId).ToList();
        Assert.Contains("S1", ids);
        Assert.Contains("V1", ids);
    }
}
