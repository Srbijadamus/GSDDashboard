using System.Security.Claims;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// The "Assign anyway" override (Overview v2 manual add) is role-gated at the endpoint
// and never overwrites sick leave. These tests prove both directly against the service
// and the role gate, independent of Auth:EnforceAuthorization.
public class WicAssignAnywayTests
{
    private static DateOnly Monday
    {
        get
        {
            var date = DateOnly.FromDateTime(DateTime.Today);
            while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
            return date;
        }
    }

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ShiftEntry SeedShift(GSDContext db, string type, string employee = "E1")
    {
        db.Employees.Add(new Employee { EmployeeId = employee, FullName = employee, IsActive = true });
        var row = new ShiftEntry { EmployeeId = employee, ShiftDate = Monday, ShiftType = type };
        db.ShiftEntries.Add(row);
        return row;
    }

    private static void SeedLocation(GSDContext db) =>
        db.WicLocations.Add(new WicLocation { LocationCode = "LOC", DisplayName = "Halle", IsActive = true });

    private static WicShiftService Svc(GSDContext db) => new(db, new AvailabilityResolver(db));

    // ── Role gate (CheckAssignAnywayAccess), config-independent ──

    private static HttpContext Anon() => new DefaultHttpContext();

    private static HttpContext AsRole(string role, string kid = "K1")
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AuthClaims.Role, role),
            new Claim(AuthClaims.Kid, kid),
        }, "test"));
        return ctx;
    }

    [Fact]
    public void AssignAnyway_Anonymous_IsUnauthorized()
    {
        var result = WicEndpointMapper.CheckAssignAnywayAccess(Anon());
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public void AssignAnyway_Agent_IsForbidden()
    {
        var result = WicEndpointMapper.CheckAssignAnywayAccess(AsRole(AppRoles.Agent));
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status403Forbidden, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public void AssignAnyway_UnknownRole_IsForbidden()
    {
        var result = WicEndpointMapper.CheckAssignAnywayAccess(AsRole("UNKNOWN"));
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status403Forbidden, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Theory]
    [InlineData("DEV")]
    [InlineData("RTM")]
    [InlineData("TEAM_LEAD")]
    public void AssignAnyway_PrivilegedRole_IsAllowed(string role)
    {
        Assert.Null(WicEndpointMapper.CheckAssignAnywayAccess(AsRole(role)));
    }

    [Fact]
    public void AssignAnyway_PrivilegedRoleWithoutKid_IsForbidden()
    {
        // The actor (KID) is required for the manual stamp; without it the gate denies.
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(AuthClaims.Role, AppRoles.Dev) }, "test"));
        Assert.Equal(StatusCodes.Status403Forbidden,
            ((IStatusCodeHttpResult)WicEndpointMapper.CheckAssignAnywayAccess(ctx)!).StatusCode);
    }

    // ── Service behaviour ──

    [Fact]
    public async Task SickLeave_IsNeverOverwritten_EvenWithAssignAnyway()
    {
        using var db = NewDb();
        var sl = SeedShift(db, "SL");
        SeedLocation(db);
        await db.SaveChangesAsync();

        var result = await Svc(db).CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", "LOC", Monday.ToString("yyyy-MM-dd"), null, null, AssignAnyway: true),
            db, manualActor: "S60028");

        var value = ((IValueHttpResult)result).Value!;
        Assert.Equal(false, value.GetType().GetProperty("saved")!.GetValue(value));
        Assert.Equal(true, value.GetType().GetProperty("skipped")!.GetValue(value));
        Assert.Equal("BLOCKING_SHIFT", value.GetType().GetProperty("reasonCode")!.GetValue(value));
        Assert.Equal("SL", sl.ShiftType);
    }

    [Fact]
    public async Task AnnualLeave_WithAssignAnyway_OverwritesAndRecordsPreviousStatus()
    {
        using var db = NewDb();
        var al = SeedShift(db, "AL");
        SeedLocation(db);
        await db.SaveChangesAsync();

        var result = await Svc(db).CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", "LOC", Monday.ToString("yyyy-MM-dd"), null, null, AssignAnyway: true),
            db, manualActor: "S60028");

        var value = ((IValueHttpResult)result).Value!;
        Assert.Equal(true, value.GetType().GetProperty("saved")!.GetValue(value));
        Assert.Equal("WIC_DUTY", al.ShiftType);
        Assert.Equal("AL", al.PreviousStatus);
    }

    [Fact]
    public async Task AnnualLeave_WithoutAssignAnyway_IsBlockedAndRecordsNothing()
    {
        using var db = NewDb();
        var al = SeedShift(db, "AL");
        SeedLocation(db);
        await db.SaveChangesAsync();

        var result = await Svc(db).CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", "LOC", Monday.ToString("yyyy-MM-dd"), null, null),
            db, manualActor: "S60028");

        var value = ((IValueHttpResult)result).Value!;
        Assert.Equal(false, value.GetType().GetProperty("saved")!.GetValue(value));
        Assert.Equal("BLOCKING_SHIFT", value.GetType().GetProperty("reasonCode")!.GetValue(value));
        Assert.Equal("AL", al.ShiftType);
        Assert.Null(al.PreviousStatus);
    }

    // Automatic writers never pass AssignAnyway, so they stay blocked even when the flag is set.
    [Fact]
    public async Task AutomaticWriter_AssignAnywayTrue_IsStillBlocked()
    {
        using var db = NewDb();
        var al = SeedShift(db, "AL");
        SeedLocation(db);
        await db.SaveChangesAsync();

        var result = await Svc(db).CreateAssignmentAsync(
            new CreateAssignmentRequest("E1", "LOC", Monday.ToString("yyyy-MM-dd"), null, null, AssignAnyway: true),
            db); // manualActor omitted → automatic writer

        var value = ((IValueHttpResult)result).Value!;
        Assert.Equal(false, value.GetType().GetProperty("saved")!.GetValue(value));
        Assert.Equal("BLOCKING_SHIFT", value.GetType().GetProperty("reasonCode")!.GetValue(value));
        Assert.Equal("AL", al.ShiftType);
    }
}
