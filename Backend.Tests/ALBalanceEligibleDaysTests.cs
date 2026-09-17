using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Employees;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// Regression tests for per-employee/per-year editable AL "eligible days"
// (EmployeeService.UpdateALEligibleDaysAsync / PATCH /api/employees/{id}/albalance/eligible).
public class ALBalanceEligibleDaysTests
{
    private static GSDContext NewDb()
    {
        var options = new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GSDContext(options);
    }

    private static void SeedEmployee(GSDContext db, string employeeId) =>
        db.Employees.Add(new Employee { EmployeeId = employeeId, FullName = "Test Agent", IsActive = true, SourceSheet = "DE" });

    [Fact]
    public async Task NewEmployee_DefaultsTo28EligibleDays_ForCurrentYear()
    {
        using var db = NewDb();
        const string empId = "E20";
        SeedEmployee(db, empId);
        await db.SaveChangesAsync();

        var svc = new EmployeeService(db);
        // Trigger lazy-create via the AL-used update path (same as first-ever AL entry).
        await svc.UpdateALBalanceAsync(empId, 0);

        var bal = db.ALBalances.Single(b => b.EmployeeId == empId);
        Assert.Equal(28m, bal.EligibleDays);
        Assert.Equal(DateTime.UtcNow.Year, bal.Year);
    }

    [Fact]
    public async Task UpdateALEligibleDaysAsync_AllowsHalfDayValues_AndRecalculatesRemaining()
    {
        using var db = NewDb();
        const string empId = "E21";
        SeedEmployee(db, empId);
        await db.SaveChangesAsync();

        var svc = new EmployeeService(db);
        await svc.UpdateALBalanceAsync(empId, 10); // PlannedTakenAL = 10, default 28 eligible

        var result = await svc.UpdateALEligibleDaysAsync(empId, 26.5m);

        Assert.NotNull(result);
        var bal = db.ALBalances.Single(b => b.EmployeeId == empId);
        Assert.Equal(26.5m, bal.EligibleDays);
        Assert.Equal(16.5m, bal.RemainingAL); // 26.5 - 10
    }

    [Fact]
    public async Task UpdateALEligibleDaysAsync_CreatesRowIfMissing()
    {
        using var db = NewDb();
        const string empId = "E22";
        SeedEmployee(db, empId);
        await db.SaveChangesAsync();

        var svc = new EmployeeService(db);
        var result = await svc.UpdateALEligibleDaysAsync(empId, 22.75m);

        Assert.NotNull(result);
        var bal = db.ALBalances.Single(b => b.EmployeeId == empId);
        Assert.Equal(22.75m, bal.EligibleDays);
        Assert.Equal(DateTime.UtcNow.Year, bal.Year);
    }

    [Fact]
    public async Task UpdateALEligibleDaysAsync_UnknownEmployee_ReturnsNull()
    {
        using var db = NewDb();
        var svc = new EmployeeService(db);

        var result = await svc.UpdateALEligibleDaysAsync("NOPE", 25m);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(28, true)]
    [InlineData(26.5, true)]
    [InlineData(22.75, true)]
    [InlineData(-1, false)]
    [InlineData(26.555, false)]
    public void IsValidEligibleDays_EnforcesNonNegativeAndMax2Decimals(decimal value, bool expected)
    {
        Assert.Equal(expected, EmployeeService.IsValidEligibleDays(value));
    }

    [Fact]
    public async Task ExistingEmployees_KeepDefault28_UntilExplicitlyEdited()
    {
        using var db = NewDb();
        const string empId = "E23";
        SeedEmployee(db, empId);
        await db.SaveChangesAsync();

        var svc = new EmployeeService(db);
        // Simulate a pre-existing balance row created by the AL migration/lazy-create path
        // with no explicit eligible-days edit yet.
        await svc.UpdateALBalanceAsync(empId, 3);

        var bal = db.ALBalances.Single(b => b.EmployeeId == empId);
        Assert.Equal(28m, bal.EligibleDays);
    }
}
