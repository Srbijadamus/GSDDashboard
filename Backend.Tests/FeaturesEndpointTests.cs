using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GSDDashboard.API;
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
/// GET /api/features is authorized and returns the flag values from configuration.
/// Defaults are false — the "flag off ⇒ identical app" contract is this default.
/// </summary>
public class FeaturesEndpointTests
{
    [Fact]
    public async Task Features_Anonymous401_AuthenticatedReturnsDefaultFalseFlags()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:OverviewV2"] = "false",
            ["Features:WhatsNew"] = "false",
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o => o.Cookie.Name = "gsd_auth");
        builder.Services.AddAuthorization();
        builder.Services.Configure<FeaturesOptions>(builder.Configuration.GetSection("Features"));

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:5058");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapFeaturesEndpoints();
        app.MapPost("/test/sign-in", async (HttpContext ctx) =>
        {
            var claims = new List<Claim>
            {
                new(AuthClaims.Name, "features-test"),
                new(AuthClaims.Role, "DEV"),
            };
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok();
        });

        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { BaseAddress = new Uri("http://127.0.0.1:5058") };

            using var anon = await client.GetAsync("/api/features");
            Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

            using var login = await client.PostAsync("/test/sign-in", null);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            using var authed = await client.GetAsync("/api/features");
            Assert.Equal(HttpStatusCode.OK, authed.StatusCode);
            var body = await authed.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
            Assert.NotNull(body);
            Assert.False(body!["overviewV2"]);
            Assert.False(body!["whatsNew"]);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Features_ReturnsTrueWhenConfigured()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:OverviewV2"] = "true",
            ["Features:WhatsNew"] = "true",
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o => o.Cookie.Name = "gsd_auth");
        builder.Services.AddAuthorization();
        builder.Services.Configure<FeaturesOptions>(builder.Configuration.GetSection("Features"));

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:5059");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapFeaturesEndpoints();
        app.MapPost("/test/sign-in", async (HttpContext ctx) =>
        {
            var claims = new List<Claim> { new(AuthClaims.Name, "t"), new(AuthClaims.Role, "DEV") };
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok();
        });

        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { BaseAddress = new Uri("http://127.0.0.1:5059") };
            using var login = await client.PostAsync("/test/sign-in", null);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            using var authed = await client.GetAsync("/api/features");
            var body = await authed.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
            Assert.NotNull(body);
            Assert.True(body!["overviewV2"]);
            Assert.True(body!["whatsNew"]);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
