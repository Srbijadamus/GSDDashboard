using System.Net;
using System.Security.Claims;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Middleware;
using GSDDashboard.API.Modules.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GSDDashboard.Tests;

/// <summary>
/// Confirms the RbacMiddleware default-deny rules in isolation. Program.cs is NOT
/// executed, so no startup SQL and no real database/IdP is touched — the same
/// isolated-host pattern as KioskProxyAccessTests.
/// </summary>
public class RbacMiddlewareTests
{
    private const string Base = "http://127.0.0.1:5057";

    private static HttpClient Client() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false
    }) { BaseAddress = new Uri(Base) };

    private static async Task<(HttpResponseMessage Login, HttpClient Client)> SignIn(string? role, string? employeeId = null, string? kid = null)
    {
        var client = Client();
        var login = await client.PostAsync(
            $"/test/sign-in?role={role}&employeeId={employeeId}&kid={kid}", null);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("gsd_auth=", StringComparison.Ordinal));
        return (login, client);
    }

    [Fact]
    public async Task EnforceTrue_AnonymousDeniedExceptAllowList_AndNonApiUntouched()
    {
        await using var app = await BuildApp(enforce: true);

        using var anon = Client();

        // /api/* is default-deny for anonymous callers.
        using var denied = await anon.GetAsync("/api/employees/E1");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Contains("Authentication required", await denied.Content.ReadAsStringAsync());

        // Anonymous allow-list still passes through (login + client-error log).
        using var login = await anon.PostAsync("/api/auth/login", null);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var clientError = await anon.PostAsync("/api/debug/client-error", null);
        Assert.Equal(HttpStatusCode.OK, clientError.StatusCode);

        // Non-/api paths are untouched by the middleware.
        using var health = await anon.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task EnforceTrue_DevUnrestricted_AdminIsDevOnly()
    {
        await using var app = await BuildApp(enforce: true);

        // DEV bypasses everything, including the admin surface.
        var (_, dev) = await SignIn(AppRoles.Dev);
        using (dev)
        {
            using var functional = await dev.GetAsync("/api/employees/E1");
            Assert.Equal(HttpStatusCode.OK, functional.StatusCode);
            using var admin = await dev.GetAsync("/api/admin/test");
            Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        }

        // TEAM_LEAD and RTM reach functional endpoints but not /api/admin.
        foreach (var role in new[] { AppRoles.TeamLead, AppRoles.Rtm })
        {
            var (_, c) = await SignIn(role);
            using (c)
            {
                using var functional = await c.GetAsync("/api/employees/E1");
                Assert.Equal(HttpStatusCode.OK, functional.StatusCode);
                using var admin = await c.GetAsync("/api/admin/test");
                Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
            }
        }
    }

    [Fact]
    public async Task EnforceTrue_AgentOwnDataOnly_NoWrites()
    {
        await using var app = await BuildApp(enforce: true);

        var (_, agent) = await SignIn(AppRoles.Agent, employeeId: "E1", kid: "K1");
        using (agent)
        {
            // Own employee record is allowed.
            using var own = await agent.GetAsync("/api/employees/E1");
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);

            // Another employee's record is denied.
            using var other = await agent.GetAsync("/api/employees/E2");
            Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);

            // Any write (non-GET) is denied.
            using var write = await agent.PostAsync("/api/some-write", null);
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);

            // Admin surface is denied.
            using var admin = await agent.GetAsync("/api/admin/test");
            Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        }
    }

    [Fact]
    public async Task EnforceTrue_AgentIcsExactMatchOnly_NotOtherMyPaths()
    {
        await using var app = await BuildApp(enforce: true);

        var (_, agent) = await SignIn(AppRoles.Agent, employeeId: "E1", kid: "K1");
        using (agent)
        {
            // The exact own-calendar address is on the AGENT allow-list.
            using var ics = await agent.GetAsync("/api/my/shifts.ics");
            Assert.Equal(HttpStatusCode.OK, ics.StatusCode);

            // A different /api/my/* address is NOT — a prefix allow would leak it, so 403.
            using var other = await agent.GetAsync("/api/my/other");
            Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        }
    }

    [Fact]
    public async Task EnforceTrue_UnknownRoleDenied()
    {
        await using var app = await BuildApp(enforce: true);

        var (_, unknown) = await SignIn("UNKNOWN");
        using (unknown)
        {
            using var functional = await unknown.GetAsync("/api/employees/E1");
            Assert.Equal(HttpStatusCode.Forbidden, functional.StatusCode);
        }
    }

    [Fact]
    public async Task EnforceFalse_MiddlewareIsNoOp_AnonymousReachesApi()
    {
        await using var app = await BuildApp(enforce: false);

        using var anon = Client();
        using var functional = await anon.GetAsync("/api/employees/E1");
        Assert.Equal(HttpStatusCode.OK, functional.StatusCode);
    }

    private static async Task<WebApplication> BuildApp(bool enforce)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:EnforceAuthorization"] = enforce ? "true" : "false"
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.Cookie.Name = "gsd_auth");
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.Urls.Add(Base);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRbac();

        // Anonymous allow-list endpoints.
        app.MapPost("/api/auth/login", () => Results.Ok()).WithTags("Auth");
        app.MapPost("/api/debug/client-error", () => Results.Ok()).WithTags("Debug");

        // Functional + admin probes (return 200; only the middleware is under test).
        app.MapGet("/api/employees/{id}", (string id) => Results.Ok()).WithTags("Employees");
        app.MapGet("/api/admin/test", () => Results.Ok()).WithTags("Admin");
        app.MapPost("/api/some-write", () => Results.Ok()).WithTags("Test");
        app.MapGet("/health", () => Results.Ok()).WithTags("Health");

        // B7 own-calendar address and a sibling /api/my/* probe (exact-match check).
        app.MapGet("/api/my/shifts.ics", () => Results.Ok()).WithTags("My");
        app.MapGet("/api/my/other", () => Results.Ok()).WithTags("My");

        // Isolated sign-in helper (issues synthetic identities; never a real login).
        app.MapPost("/test/sign-in", async (string? role, string? employeeId, string? kid, HttpContext ctx) =>
        {
            var claims = new List<Claim> { new(AuthClaims.Name, "rbac-test") };
            if (!string.IsNullOrEmpty(role)) claims.Add(new Claim(AuthClaims.Role, role));
            if (!string.IsNullOrEmpty(employeeId)) claims.Add(new Claim(AuthClaims.EmployeeId, employeeId));
            if (!string.IsNullOrEmpty(kid)) claims.Add(new Claim(AuthClaims.Kid, kid));
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok();
        });

        await app.StartAsync();
        return app;
    }
}
