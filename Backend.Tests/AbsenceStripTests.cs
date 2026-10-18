using System.Net;
using System.Net.Http.Json;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Vacations;
using GSDDashboard.API.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GSDDashboard.Tests;

/// <summary>
/// B4 (Features:AbsenceStrip) — GET /api/vacations/absence-strip.
/// Per day it returns the ACTIVE employees on annual leave (Vacations) and on sick leave
/// (SickLeaves). Inactive employees must not be counted.
/// </summary>
public class AbsenceStripTests
{
    private static async Task<WebApplication> BuildAppAsync(string database)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<GSDContext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddScoped<VacationService>();
        builder.Services.AddScoped<ShiftSyncService>();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapVacationEndpoints();

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

    [Fact]
    public async Task Strip_CountsActiveLeaveAndSick_ExcludesInactive()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            var d = DateOnly.FromDateTime(DateTime.Today).AddDays(5);
            var iso = d.ToString("yyyy-MM-dd");

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
                db.Employees.AddRange(
                    new Employee { EmployeeId = "E1", FullName = "On leave", IsActive = true },
                    new Employee { EmployeeId = "E2", FullName = "Sick", IsActive = true },
                    new Employee { EmployeeId = "E3", FullName = "Inactive on leave", IsActive = false });
                db.Vacations.Add(new Vacation { EmployeeId = "E1", FirstDay = d, LastDay = d });
                db.Vacations.Add(new Vacation { EmployeeId = "E3", FirstDay = d, LastDay = d });
                db.SickLeaves.Add(new SickLeave { EmployeeId = "E2", FirstDay = d, LastDay = d });
                await db.SaveChangesAsync();
            }

            using var client = ClientFor(app);
            using var res = await client.GetAsync($"/api/vacations/absence-strip?from={iso}&to={iso}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var days = await res.Content.ReadFromJsonAsync<List<AbsenceStripDayDto>>();
            Assert.NotNull(days);
            var day = Assert.Single(days!);
            Assert.Equal(iso, day.Date);
            Assert.Contains("E1", day.Urlaub);
            Assert.DoesNotContain("E3", day.Urlaub);   // inactive — not in the plan, not counted
            Assert.Contains("E2", day.Krank);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Strip_DayWithoutAbsence_ReturnsEmptyLists()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            var d = DateOnly.FromDateTime(DateTime.Today).AddDays(8);
            var iso = d.ToString("yyyy-MM-dd");

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
                db.Employees.Add(new Employee { EmployeeId = "E1", FullName = "Present", IsActive = true });
                await db.SaveChangesAsync();
            }

            using var client = ClientFor(app);
            using var res = await client.GetAsync($"/api/vacations/absence-strip?from={iso}&to={iso}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var days = await res.Content.ReadFromJsonAsync<List<AbsenceStripDayDto>>();
            var day = Assert.Single(days!);
            Assert.Empty(day.Urlaub);
            Assert.Empty(day.Krank);
        }
        finally { await app.StopAsync(); }
    }
}
