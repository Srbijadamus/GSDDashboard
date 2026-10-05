using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.Shifts;
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

public class ManualShiftEditTests
{
    private static readonly string[] Types =
        ["WORKING", "WIC_DUTY", "AL", "HALF_AL", "SL", "UL", "OL", "TRAINING",
         "OFF", "OFF_WEEKEND", "PH", "LPH", "CD", "CO", "RESIGNED", "EMPTY"];

    [Fact]
    public async Task ManualRoutes_AllCellStatuses_SaveTimesAndSessionActor_AndRejectUnauthorizedUsers()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:EnforceAuthorization"] = "false" });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<GSDContext>(options => options.UseInMemoryDatabase(database));
        builder.Services.AddScoped<ShiftService>();
        builder.Services.AddScoped<ShiftValidationService>();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseAuthentication();
        app.MapShiftEndpoints();
        app.MapPost("/test/login", async (string role, HttpContext ctx) =>
        {
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(AuthClaims.Kid, "TEST-OPERATOR"), new Claim(AuthClaims.Role, role)],
                    CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok();
        });
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            db.Employees.Add(new Employee { EmployeeId = "TEST-E", FullName = "Synthetic agent", IsActive = true });
            db.ShiftEntries.Add(new ShiftEntry { Id = 101, EmployeeId = "TEST-E", ShiftDate = DateOnly.FromDateTime(DateTime.Today), ShiftType = "SL" });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        HttpClient Client() => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
        try
        {
            using var anonymous = Client();
            using var denied = await anonymous.PatchAsJsonAsync("/api/shifts/101/manual", new { shiftType = "WORKING" });
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using var deniedAssign = await anonymous.PostAsJsonAsync("/api/shifts/assign/manual", new { employeeId = "TEST-E", shiftDate = "2026-10-06", shiftType = "WORKING" });
            Assert.Equal(HttpStatusCode.Unauthorized, deniedAssign.StatusCode);
            using var agent = Client();
            using var agentLogin = await agent.PostAsync("/test/login?role=AGENT", null);
            using var forbidden = await agent.PatchAsJsonAsync("/api/shifts/101/manual", new { shiftType = "WORKING" });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            foreach (var role in new[] { "DEV", "RTM", "TEAM_LEAD" })
            {
                using var client = Client();
                using var login = await client.PostAsync("/test/login?role=" + role, null);
                foreach (var type in Types)
                {
                    using var saved = await client.PatchAsJsonAsync("/api/shifts/101/manual",
                        new { shiftType = type, shiftStart = "09:00", shiftEnd = "17:00", manualChangeBy = "FORGED" });
                    Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
                    using var scope = app.Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
                    var row = await db.ShiftEntries.SingleAsync();
                    Assert.Equal(type, row.ShiftType);
                    Assert.Equal("09:00", row.ShiftStart);
                    Assert.Equal("17:00", row.ShiftEnd);
                    Assert.Equal(type == "WIC_DUTY", row.IsWicDuty);
                    Assert.Equal("TEST-OPERATOR", row.ManualChangeBy);
                    Assert.NotNull(row.ManualChangeAt);
                }
            }
            using var operatorClient = Client();
            using var operatorLogin = await operatorClient.PostAsync("/test/login?role=RTM", null);
            using var assigned = await operatorClient.PostAsJsonAsync("/api/shifts/assign/manual",
                new { employeeId = "TEST-E", shiftDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"), shiftType = "WIC_DUTY", shiftStart = "10:00", shiftEnd = "18:00" });
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
            using var finalScope = app.Services.CreateScope();
            var created = await finalScope.ServiceProvider.GetRequiredService<GSDContext>().ShiftEntries.OrderBy(x => x.ShiftDate).LastAsync();
            Assert.True(created.IsWicDuty);
            Assert.Equal("TEST-OPERATOR", created.ManualChangeBy);
            using var reassigned = await operatorClient.PostAsJsonAsync("/api/shifts/assign/manual",
                new { employeeId = "TEST-E", shiftDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"), shiftType = "WIC_DUTY", shiftStart = "11:00", shiftEnd = "19:00" });
            Assert.Equal(HttpStatusCode.OK, reassigned.StatusCode);
            using var legacyDuplicate = await operatorClient.PostAsJsonAsync("/api/shifts/assign",
                new { employeeId = "TEST-E", shiftDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"), shiftType = "WIC_DUTY" });
            Assert.Equal(HttpStatusCode.Conflict, legacyDuplicate.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task ManualWicToWorking_ClearsOnlyEditedRowsDutyLink_AndLegacyRouteKeepsItsBehavior()
    {
        using var db = new GSDContext(new DbContextOptionsBuilder<GSDContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Employees.Add(new Employee { EmployeeId = "TEST-E", FullName = "Synthetic agent", IsActive = true });
        var row = new ShiftEntry { EmployeeId = "TEST-E", ShiftDate = DateOnly.FromDateTime(DateTime.Today),
            ShiftType = "WIC_DUTY", IsWicDuty = true, AgentTask = "Synthetic centre", LocationId = "TEST-CENTRE", AssignmentStatus = "ASSIGNED" };
        var untouched = new ShiftEntry { EmployeeId = "TEST-E", ShiftDate = row.ShiftDate.AddDays(1), ShiftType = "SL", AgentTask = "Synthetic centre" };
        var legacy = new ShiftEntry { EmployeeId = "TEST-E", ShiftDate = row.ShiftDate.AddDays(2), ShiftType = "SL" };
        db.ShiftEntries.AddRange(row, untouched, legacy);
        await db.SaveChangesAsync();
        var service = new ShiftService(db);
        var result = await service.UpdateShiftAsync(row.Id, new ShiftUpdateDto("WORKING", "09:00", "17:00", null, null, null), "TEST-OPERATOR");
        Assert.Null(result.Error);
        Assert.False(row.IsWicDuty);
        Assert.Null(row.AgentTask);
        Assert.Null(row.LocationId);
        Assert.Null(untouched.ManualChangeBy);
        Assert.Equal("SL", untouched.ShiftType);
        await service.UpdateShiftAsync(untouched.Id, new ShiftUpdateDto("WORKING", "08:00", "16:00", null, null, null), "TEST-OPERATOR");
        Assert.Null(untouched.AgentTask);
        Assert.Equal("TEST-OPERATOR", untouched.ManualChangeBy);
        await service.UpdateShiftAsync(legacy.Id, new ShiftUpdateDto("WORKING", "08:00", "16:00", null, null, null));
        Assert.Null(legacy.ManualChangeBy);
    }
}
