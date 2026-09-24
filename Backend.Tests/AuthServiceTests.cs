using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GSDDashboard.Tests;

// Login resolution rules (odgovor3.md decision 2):
//   a) AppUserRoles, IsActive=1, Kid non-empty exact match â†’ that role (EmployeeId may be NULL)
//   b) Employees active, PrimaryKid or SecondaryKid exact match â†’ AGENT
//   c) Employees active, EmployeeId exact match â†’ AGENT
//   d) otherwise â†’ null (401 "Unknown KID")
// More than one active match at any step â†’ null, never guess.
public class AuthServiceTests
{
    private static GSDContext NewDb()
    {
        var options = new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GSDContext(options);
    }

    private static AuthService NewSvc(GSDContext db) => new(db, NullLogger<AuthService>.Instance);

    private static Employee Emp(string id, string? primaryKid = null, string? secondaryKid = null, bool active = true) =>
        new() { EmployeeId = id, FullName = $"Agent {id}", PrimaryKid = primaryKid, SecondaryKid = secondaryKid, IsActive = active };

    [Fact]
    public async Task RuleA_ActiveAppUserRole_WinsWithItsRole()
    {
        using var db = NewDb();
        db.AppUserRoles.Add(new AppUserRole { Kid = "S60028", Role = AppRoles.Rtm, DisplayName = "Silvija Angelova", IsActive = true });
        await db.SaveChangesAsync();

        var result = await NewSvc(db).ResolveLoginAsync("S60028");

        Assert.NotNull(result);
        Assert.Equal(AppRoles.Rtm, result!.Role);
        Assert.Null(result.EmployeeId);
        Assert.Equal("Silvija Angelova", result.DisplayName);
    }

    [Fact]
    public async Task RuleA_AppUserRolesTakesPrecedenceOverEmployees()
    {
        using var db = NewDb();
        db.AppUserRoles.Add(new AppUserRole { Kid = "X1", Role = AppRoles.Dev, EmployeeId = "9001", DisplayName = "Dev", IsActive = true });
        db.Employees.Add(Emp("9001", primaryKid: "X1"));
        await db.SaveChangesAsync();

        var result = await NewSvc(db).ResolveLoginAsync("X1");

        Assert.Equal(AppRoles.Dev, result!.Role);
    }

    [Fact]
    public async Task RuleA_InactiveRow_DoesNotAuthenticate()
    {
        using var db = NewDb();
        db.AppUserRoles.Add(new AppUserRole { Kid = "S60028", Role = AppRoles.Rtm, DisplayName = "Silvija Angelova", IsActive = false });
        await db.SaveChangesAsync();

        Assert.Null(await NewSvc(db).ResolveLoginAsync("S60028"));
    }

    [Fact]
    public async Task RuleA_EmptyKidPlaceholder_NeverAuthenticates()
    {
        using var db = NewDb();
        // TEAM_LEAD placeholder rows: Kid = '' â€” must never match, even via rule (a).
        db.AppUserRoles.Add(new AppUserRole { Kid = "", Role = AppRoles.TeamLead, DisplayName = "Tobias Rossberg", IsActive = false });
        await db.SaveChangesAsync();

        Assert.Null(await NewSvc(db).ResolveLoginAsync(""));
        Assert.Null(await NewSvc(db).ResolveLoginAsync("   "));
        Assert.Null(await NewSvc(db).ResolveLoginAsync("Tobias Rossberg"));
    }

    [Fact]
    public async Task RuleA_AgentRowWithoutEmployeeId_IsRejected()
    {
        using var db = NewDb();
        db.AppUserRoles.Add(new AppUserRole { Kid = "A1", Role = AppRoles.Agent, EmployeeId = null, DisplayName = "Bad", IsActive = true });
        await db.SaveChangesAsync();

        Assert.Null(await NewSvc(db).ResolveLoginAsync("A1"));
    }
    [Fact]
    public async Task RuleB_PrimaryKid_ResolvesToAgent()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("9074573", primaryKid: "K37144"));
        await db.SaveChangesAsync();

        var result = await NewSvc(db).ResolveLoginAsync("K37144");

        Assert.NotNull(result);
        Assert.Equal(AppRoles.Agent, result!.Role);
        Assert.Equal("9074573", result.EmployeeId);
    }

    [Fact]
    public async Task RuleB_SecondaryKid_ResolvesToAgent()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("9074573", primaryKid: "K37144", secondaryKid: "K37200"));
        await db.SaveChangesAsync();

        var result = await NewSvc(db).ResolveLoginAsync("K37200");

        Assert.Equal("9074573", result!.EmployeeId);
    }

    [Fact]
    public async Task RuleC_EmployeeId_ResolvesToAgent()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("9074573")); // 63 active agents have no KID at all
        await db.SaveChangesAsync();

        var result = await NewSvc(db).ResolveLoginAsync("9074573");

        Assert.NotNull(result);
        Assert.Equal(AppRoles.Agent, result!.Role);
        Assert.Equal("9074573", result.EmployeeId);
    }

    [Fact]
    public async Task RuleD_UnknownKid_ReturnsNull()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("9074573", primaryKid: "K37144"));
        await db.SaveChangesAsync();

        Assert.Null(await NewSvc(db).ResolveLoginAsync("NOPE1"));
    }

    [Fact]
    public async Task InactiveEmployee_NeverAuthenticates()
    {
        using var db = NewDb();
        db.Employees.Add(Emp("9074573", primaryKid: "K37144", active: false));
        await db.SaveChangesAsync();

        Assert.Null(await NewSvc(db).ResolveLoginAsync("K37144"));
        Assert.Null(await NewSvc(db).ResolveLoginAsync("9074573"));
    }

    [Fact]
    public async Task DuplicateKidAcrossActiveEmployees_IsRejected_NeverGuessed()
    {
        using var db = NewDb();
        // Corrupt state: same KID on two active employees (queries 4/4b say this
        // cannot happen today â€” but if it ever does, login must refuse, not guess).
        db.Employees.Add(Emp("9001", primaryKid: "DUP1"));
        db.Employees.Add(Emp("9002", secondaryKid: "DUP1"));
        await db.SaveChangesAsync();

        Assert.Null(await NewSvc(db).ResolveLoginAsync("DUP1"));
    }

    [Fact]
    public async Task InputLongerThanKidColumn_IsRejected()
    {
        using var db = NewDb();
        Assert.Null(await NewSvc(db).ResolveLoginAsync(new string('X', 21)));
    }
}
