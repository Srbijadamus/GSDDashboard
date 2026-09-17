using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Employees;
using GSDDashboard.API.Modules.Shifts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// Regression tests for the Primary Role list (Add/Edit Agent dialog + BLUEPRINT_DATA_MODEL.md).
// VWIC was documented but missing from the dropdown; this also covers the other
// documented/live-data roles so the whitelist stays in sync going forward.
public class PrimaryRoleTests
{
    private static GSDContext NewDb()
    {
        var options = new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GSDContext(options);
    }

    [Theory]
    [InlineData("Voice")]
    [InlineData("SSP")]
    [InlineData("Chat")]
    [InlineData("Chat CRO")]
    [InlineData("Dispatcher")]
    [InlineData("SME")]
    [InlineData("WIC")]
    [InlineData("Bulk PWs")]
    [InlineData("VWIC")]
    [InlineData("2nd Level")]
    [InlineData("Booking Tool")]
    [InlineData("Trainer")]
    public async Task CreateAsync_AcceptsEveryDocumentedOrLiveDataRole(string role)
    {
        using var db = NewDb();
        var svc = new EmployeeService(db);

        var dto = new CreateEmployeeDto("E-" + role.Replace(" ", ""), "Test Agent", "Full Time", role, "Team A", null, "GSD_DE", null);
        var result = await svc.CreateAsync(dto);

        Assert.NotNull(result);
        Assert.Equal(role, result!.PrimaryRole);
    }

    [Fact]
    public async Task UpdateAsync_KeepsUndocumentedExistingRole_WhenNotChanged()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Legacy Agent", PrimaryRole = "Legacy Role Not In List", IsActive = true });
        await db.SaveChangesAsync();

        var svc = new EmployeeService(db);
        var result = await svc.UpdateAsync("E1", new UpdateEmployeeDto("Legacy Agent Updated", null, null, null, null, null, null));

        Assert.NotNull(result);
        Assert.Equal("Legacy Role Not In List", result!.PrimaryRole);
    }

    [Fact]
    public void DefaultFor_VWIC_ReturnsVwicTask()
    {
        Assert.Equal("VWIC", AgentTasks.DefaultFor("VWIC"));
    }
}
