using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Middleware;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GSDDashboard.Tests;

public class KioskProxyAccessTests
{
    [Fact]
    public async Task Attendance_Anonymous401_SupervisorCookie200_RegardlessOfRbacFlag()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:EnforceAuthorization"] = "false"
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.Cookie.Name = "gsd_auth");
        builder.Services.AddAuthorization();
        var proxy = new RecordingProxy();
        builder.Services.AddSingleton<IKioskProxy>(proxy);

        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:5056");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRbac();
        app.MapKioskEndpoints();
        // Only this isolated host can issue synthetic test identities; Program.cs
        // is not executed, so no startup SQL or real auth provider is reachable.
        app.MapPost("/test/sign-in", async (string role, HttpContext ctx) =>
        {
            var claims = new List<Claim> { new(AuthClaims.Name, "proxy-test") };
            if (role != "missing")
                claims.Add(new Claim(AuthClaims.Role, role));
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims,
                    CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok();
        });
        await app.StartAsync();

        try
        {
            foreach (var enforce in new[] { "false", "true" })
            {
                builder.Configuration["Auth:EnforceAuthorization"] = enforce;
                using var anonymous = CreateClient();
                var before = proxy.Calls;
                using var denied = await anonymous.GetAsync("/api/kiosk/attendance");
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
                Assert.Contains("Authentication required", await denied.Content.ReadAsStringAsync());
                Assert.Equal(before, proxy.Calls);
                using var deniedAgents = await anonymous.GetAsync("/api/kiosk/agents");
                Assert.Equal(HttpStatusCode.Unauthorized, deniedAgents.StatusCode);
                using var deniedAction = await anonymous.PostAsJsonAsync("/api/kiosk/manual-checkin", new { agentId = 1 });
                Assert.Equal(HttpStatusCode.Unauthorized, deniedAction.StatusCode);

                foreach (var role in new[] { AppRoles.Dev, AppRoles.Rtm, AppRoles.TeamLead,
                                             AppRoles.Agent, "missing", "UNKNOWN" })
                {
                    using var signedIn = CreateClient();
                    using var login = await signedIn.PostAsync(
                        $"/test/sign-in?role={role}", null);
                    Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                    Assert.Contains(login.Headers.GetValues("Set-Cookie"),
                        cookie => cookie.StartsWith("gsd_auth=", StringComparison.Ordinal));

                    before = proxy.Calls;
                    using var response = await signedIn.GetAsync(
                        "/api/kiosk/attendance?date=2026-10-03&tl=Test%20Lead");
                    if (role is AppRoles.Dev or AppRoles.Rtm or AppRoles.TeamLead)
                    {
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                        Assert.Equal(RecordingProxy.Payload, await response.Content.ReadAsStringAsync());
                        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
                        Assert.Equal(before + 1, proxy.Calls);
                        Assert.Equal("2026-10-03", proxy.Date);
                        Assert.Equal("Test Lead", proxy.TeamLead);
                        using var agents = await signedIn.GetAsync("/api/kiosk/agents");
                        Assert.Equal(HttpStatusCode.OK, agents.StatusCode);
                        foreach (var path in new[] { "/api/kiosk/manual-checkin", "/api/kiosk/manual-logout" })
                        {
                            using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, path)
                            {
                                Content = JsonContent.Create(new { agentId = 1 })
                            };
                            crossOrigin.Headers.Add("Origin", "http://untrusted.invalid");
                            crossOrigin.Headers.Add("X-Pulse-Request", "1");
                            var beforeDenied = proxy.Calls;
                            using var rejected = await signedIn.SendAsync(crossOrigin);
                            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
                            Assert.Equal(beforeDenied, proxy.Calls);
                            using var request = new HttpRequestMessage(HttpMethod.Post, path)
                            {
                                Content = JsonContent.Create(new { agentId = 1, performedBy = "forged" })
                            };
                            request.Headers.Add("Origin", "http://127.0.0.1:5056");
                            request.Headers.Add("X-Pulse-Request", "1");
                            using var written = await signedIn.SendAsync(request);
                            Assert.Equal(HttpStatusCode.OK, written.StatusCode);
                        }
                    }
                    else
                    {
                        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                        Assert.Equal(before, proxy.Calls);
                        using var agents = await signedIn.GetAsync("/api/kiosk/agents");
                        Assert.Equal(HttpStatusCode.Forbidden, agents.StatusCode);
                        using var action = await signedIn.PostAsJsonAsync("/api/kiosk/manual-logout", new { agentId = 1 });
                        Assert.Equal(HttpStatusCode.Forbidden, action.StatusCode);
                    }
                }
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false
    }) { BaseAddress = new Uri("http://127.0.0.1:5056") };

    private sealed class RecordingProxy : IKioskProxy
    {
        public const string Payload = "[{\"employee_id\":\"TEST-ONLY\",\"status\":\"ON_SHIFT\"}]";
        public bool IsConfigured => true;
        public int Calls { get; private set; }
        public string? Date { get; private set; }
        public string? TeamLead { get; private set; }
        public Task<(int Status, string Body)> GetAgentsAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult((200, "[{\"id\":1}]"));
        }
        public Task<(int Status, string Body)> ManualActionAsync(bool checkin, int agentId,
            string performedBy, CancellationToken ct = default)
        {
            Calls++;
            Assert.Equal("proxy-test", performedBy);
            return Task.FromResult((200, "{\"success\":true}"));
        }

        public Task<(int Status, string Body)> GetAttendanceAsync(string? date, string? tl,
                                                                 CancellationToken ct = default)
        {
            Calls++;
            Date = date;
            TeamLead = tl;
            return Task.FromResult((StatusCodes.Status200OK, Payload));
        }
    }
}
