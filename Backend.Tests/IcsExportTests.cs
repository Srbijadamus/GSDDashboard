using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Middleware;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.MyShifts;
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

namespace GSDDashboard.Tests;

/// <summary>
/// B7 (Features:IcsExport) — GET /api/my/shifts.ics.
///
/// Proves the endpoint's own contract on a fresh in-memory database:
///   - anonymous always 401 (even while Auth:EnforceAuthorization=false),
///   - only the session's own shifts are exported (two users never see each other),
///   - a non-parsable time degrades to an all-day event, never a 500,
///   - a role without an EmployeeId gets 200 with an empty calendar,
///   - under EnforceAuthorization=true an AGENT is allowed (RBAC allow-list).
/// </summary>
public class IcsExportTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private static async Task<WebApplication> BuildAppAsync(string database, bool enforce)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:EnforceAuthorization"] = enforce ? "true" : "false",
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddAuthorization();
        builder.Services.AddDbContext<GSDContext>(o => o.UseInMemoryDatabase(database));

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseAuthentication();
        app.UseAuthorization();
        if (enforce) app.UseRbac();
        app.MapMyEndpoints();
        app.MapPost("/test/sign-in", async (string? role, string? employeeId, HttpContext ctx) =>
        {
            var claims = new List<Claim> { new(AuthClaims.Name, "ics-test") };
            if (!string.IsNullOrEmpty(role)) claims.Add(new Claim(AuthClaims.Role, role));
            if (!string.IsNullOrEmpty(employeeId)) claims.Add(new Claim(AuthClaims.EmployeeId, employeeId));
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok();
        });

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            db.Employees.AddRange(
                new Employee { EmployeeId = "E1", FullName = "Synthetic One", IsActive = true },
                new Employee { EmployeeId = "E2", FullName = "Synthetic Two", IsActive = true });
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

    private static async Task AddShiftAsync(WebApplication app, string employeeId, DateOnly date, string shiftType, string? start = null, string? end = null)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = employeeId,
            ShiftDate = date,
            ShiftType = shiftType,
            ShiftStart = start,
            ShiftEnd = end,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SignIn(HttpClient client, string role, string employeeId)
    {
        using var res = await client.PostAsync($"/test/sign-in?role={role}&employeeId={employeeId}", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Always401_EvenWhenEnforcementOff()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString(), enforce: false);
        try
        {
            using var client = ClientFor(app);
            using var res = await client.GetAsync("/api/my/shifts.ics");
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task EachUserSeesOnlyTheirOwnShifts()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString(), enforce: false);
        try
        {
            await AddShiftAsync(app, "E1", Today.AddDays(1), "WORKING", "08:00", "16:00");
            await AddShiftAsync(app, "E1", Today.AddDays(3), "AL");
            await AddShiftAsync(app, "E2", Today.AddDays(2), "SL");

            // User one sees only WORKING + AL, never SL.
            using var one = ClientFor(app);
            await SignIn(one, AppRoles.Agent, "E1");
            var sw = Stopwatch.StartNew();
            using var resOne = await one.GetAsync("/api/my/shifts.ics");
            sw.Stop();
            Assert.Equal(HttpStatusCode.OK, resOne.StatusCode);
            Assert.Equal("text/calendar", resOne.Content.Headers.ContentType?.MediaType);
            Assert.NotNull(resOne.Content.Headers.ContentDisposition);
            var icsOne = await resOne.Content.ReadAsStringAsync();
            Assert.Contains("BEGIN:VCALENDAR", icsOne);
            Assert.Contains("SUMMARY:WORKING", icsOne);
            Assert.Contains("SUMMARY:AL", icsOne);
            Assert.DoesNotContain("SUMMARY:SL", icsOne);
            Assert.True(sw.ElapsedMilliseconds < 5_000, $"export took {sw.ElapsedMilliseconds}ms");

            // User two sees only SL, never WORKING/AL.
            using var two = ClientFor(app);
            await SignIn(two, AppRoles.Agent, "E2");
            using var resTwo = await two.GetAsync("/api/my/shifts.ics");
            Assert.Equal(HttpStatusCode.OK, resTwo.StatusCode);
            var icsTwo = await resTwo.Content.ReadAsStringAsync();
            Assert.Contains("SUMMARY:SL", icsTwo);
            Assert.DoesNotContain("SUMMARY:WORKING", icsTwo);
            Assert.DoesNotContain("SUMMARY:AL", icsTwo);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task NonParsableTime_DegradesToAllDay_Never500()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString(), enforce: false);
        try
        {
            await AddShiftAsync(app, "E1", Today.AddDays(1), "WORKING", "not-a-time", "also-bad");

            using var client = ClientFor(app);
            await SignIn(client, AppRoles.Agent, "E1");
            using var res = await client.GetAsync("/api/my/shifts.ics");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var ics = await res.Content.ReadAsStringAsync();
            Assert.Contains("BEGIN:VCALENDAR", ics);
            // The row is still exported, as an all-day event.
            Assert.Contains("SUMMARY:WORKING", ics);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task RoleWithoutEmployeeId_GetsEmptyCalendar200()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString(), enforce: false);
        try
        {
            await AddShiftAsync(app, "E1", Today.AddDays(1), "WORKING", "08:00", "16:00");

            using var client = ClientFor(app);
            await SignIn(client, AppRoles.Rtm, "");
            using var res = await client.GetAsync("/api/my/shifts.ics");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var ics = await res.Content.ReadAsStringAsync();
            Assert.Contains("BEGIN:VCALENDAR", ics);
            Assert.DoesNotContain("BEGIN:VEVENT", ics);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task EnforceTrue_AgentAllowed_AnonymousDenied()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString(), enforce: true);
        try
        {
            await AddShiftAsync(app, "E1", Today.AddDays(1), "WORKING", "08:00", "16:00");

            using var anon = ClientFor(app);
            using var denied = await anon.GetAsync("/api/my/shifts.ics");
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

            using var agent = ClientFor(app);
            await SignIn(agent, AppRoles.Agent, "E1");
            using var allowed = await agent.GetAsync("/api/my/shifts.ics");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        finally { await app.StopAsync(); }
    }
}
