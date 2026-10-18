using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

/// <summary>
/// Endpoint-level auth for POST /api/wic/assignments (Phase B hardening): anonymous is
/// 401 even while Auth:EnforceAuthorization is false; any authenticated role proceeds
/// exactly as before; the AssignAnyway role gate is unchanged.
/// </summary>
public class WicAssignmentsAuthTests
{
    private const string Operator = "TEST-OPERATOR";

    private static async Task<WebApplication> BuildAppAsync(string database)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:EnforceAuthorization"] = "false",
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddDbContext<GSDContext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddScoped<WicShiftService>();
        builder.Services.AddScoped<AvailabilityResolver>();
        builder.Services.AddScoped<ShiftValidationService>();
        builder.Services.AddScoped<ShiftSyncService>();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseAuthentication();
        app.MapWicEndpoints();
        app.MapPost("/test/login", async (HttpContext ctx) =>
        {
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(AuthClaims.Kid, Operator),
                    new Claim(AuthClaims.Role, AppRoles.Rtm),
                }, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok();
        });

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Synthetic", IsActive = true });
            db.WicLocations.Add(new WicLocation { LocationCode = "LOC", DisplayName = "Halle", IsActive = true });
            await db.SaveChangesAsync();
        }

        await app.StartAsync();
        return app;
    }

    private static HttpClient ClientFor(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(address),
        };
    }

    private static object Body() => new
    {
        employeeId = "E1",
        locationCode = "LOC",
        date = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
        shiftStart = (string?)null,
        shiftEnd = (string?)null,
    };

    [Fact]
    public async Task Assignments_Anonymous_Is401_EvenWhenEnforcementOff()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var res = await client.PostAsJsonAsync("/api/wic/assignments", Body());
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Assignments_LoggedIn_NoAssignAnyway_StillSucceeds()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var login = await client.PostAsync("/test/login", null);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            using var res = await client.PostAsJsonAsync("/api/wic/assignments", Body());
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E1");
            Assert.True(wic.IsOnSite);
        }
        finally { await app.StopAsync(); }
    }
}
