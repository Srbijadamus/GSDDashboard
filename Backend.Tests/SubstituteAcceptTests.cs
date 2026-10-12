using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.SubstituteAccept;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GSDDashboard.Tests;

// The accept-substitute route must answer every rejection with the exact reason; the
// live 2026-10-12 failure showed only the generic "Failed to accept substitute" while
// the server had already refused a protected manual entry (ShiftEntries id 56515,
// employee 9074345 = Angelika Weber, WORKING/GSD, ManualChangeBy S60028).
//
// These tests pin the four situations: a day whose place is already filled, an agent
// with an existing (non-manual) assignment that day, a protected manual entry, and a
// clean successful accept.
public class SubstituteAcceptTests
{
    private static readonly DateOnly Day = new(2026, 10, 12); // Monday

    private static async Task<(WebApplication app, HttpClient client)> StartAsync(Action<GSDContext> seed)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<GSDContext>(o => o.UseInMemoryDatabase(database));
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapSubstituteAcceptEndpoints();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            db.Employees.Add(new Employee { EmployeeId = "9074345", FullName = "Angelika Weber", IsActive = true });
            db.WicLocations.Add(new WicLocation { LocationCode = "LOC", DisplayName = "Centre", IsActive = true });
            seed(db);
            await db.SaveChangesAsync();
        }

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
        return (app, client);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> AcceptAsync(HttpClient client, string employeeId = "9074345")
    {
        using var res = await client.PostAsJsonAsync("/api/wic/substitutes/accept", new
        {
            employeeId,
            locationCode = "LOC",
            date = Day.ToString("yyyy-MM-dd"),
            shiftStart = (string?)null,
            shiftEnd = (string?)null,
            sourceType = "BACKUP",
        });
        var text = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return (res.StatusCode, doc.RootElement.Clone());
    }

    private static string Error(JsonElement body) => body.GetProperty("error").GetString()!;
    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    [Fact]
    public async Task CleanDay_Accept_WritesShiftEntryWicEntryAndHistory()
    {
        var (app, client) = await StartAsync(_ => { });
        try
        {
            var (status, body) = await AcceptAsync(client);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.True(body.GetProperty("success").GetBoolean());

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            var shift = await db.ShiftEntries.SingleAsync();
            Assert.Equal(ShiftTypes.WicDuty, shift.ShiftType);
            Assert.True(shift.IsWicDuty);
            Assert.Equal("Centre", shift.AgentTask);
            var wic = await db.WicShiftEntries.SingleAsync();
            Assert.True(wic.IsOnSite);
            Assert.Equal("Centre", wic.SupportLocation);
            Assert.Equal("WIC", wic.Task);
            var history = await db.SubstitutionHistory.SingleAsync();
            Assert.Equal("9074345", history.EmployeeId);
            Assert.Equal("LOC", history.LocationCode);
            Assert.Equal(Day, history.Date);
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }

    // An existing, non-manual assignment for the same day (e.g. a roster WORKING row)
    // is what a substitute accept is for: it is converted to the WIC duty.
    [Fact]
    public async Task ExistingSameDayAssignment_IsConvertedToWicDuty()
    {
        var (app, client) = await StartAsync(db =>
        {
            db.ShiftEntries.Add(new ShiftEntry
            {
                Id = 101, EmployeeId = "9074345", ShiftDate = Day, ShiftType = ShiftTypes.Working,
                AgentTask = "GSD", AutoGenerated = true, SourceModule = "Roster",
            });
            db.WicShiftEntries.Add(new WicShiftEntry
            {
                Id = 201, EmployeeId = "9074345", ShiftDate = Day, DayOfWeek = "Monday",
                SupportLocation = null, IsOnSite = false, IsOffDay = true, Task = "WIC",
            });
        });
        try
        {
            var (status, _) = await AcceptAsync(client);
            Assert.Equal(HttpStatusCode.OK, status);

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            Assert.Equal(ShiftTypes.WicDuty, (await db.ShiftEntries.SingleAsync(s => s.Id == 101)).ShiftType);
            // The lone NULL-location entry is a stale orphan and is replaced by the
            // on-site row for the centre — exactly one row remains.
            var wic = await db.WicShiftEntries.SingleAsync();
            Assert.True(wic.IsOnSite);
            Assert.Equal("Centre", wic.SupportLocation);
            Assert.Single(await db.SubstitutionHistory.ToListAsync());
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }

    // The place is already filled and recorded: the on-site WIC row is reused and the
    // stale NULL-location orphan is removed — no duplicate row, no duplicate history.
    [Fact]
    public async Task WicEntryAlreadyFilled_IsUpserted_WithoutDuplicate()
    {
        var (app, client) = await StartAsync(db =>
        {
            db.ShiftEntries.Add(new ShiftEntry
            {
                Id = 102, EmployeeId = "9074345", ShiftDate = Day, ShiftType = ShiftTypes.WicDuty,
                IsWicDuty = true, AgentTask = "Centre", AutoGenerated = true, SourceModule = "SUBSTITUTE",
            });
            db.WicShiftEntries.Add(new WicShiftEntry
            {
                Id = 202, EmployeeId = "9074345", ShiftDate = Day, DayOfWeek = "Monday",
                SupportLocation = "Centre", IsOnSite = true, Task = "WIC",
            });
            db.WicShiftEntries.Add(new WicShiftEntry
            {
                Id = 203, EmployeeId = "9074345", ShiftDate = Day, DayOfWeek = "Monday",
                SupportLocation = null, IsOnSite = false, IsOffDay = true, Task = "WIC",
            });
        });
        try
        {
            var (status, _) = await AcceptAsync(client);
            Assert.Equal(HttpStatusCode.OK, status);

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            var wic = await db.WicShiftEntries.SingleAsync();
            Assert.Equal(202, wic.Id);
            Assert.True(wic.IsOnSite);
            Assert.Single(await db.SubstitutionHistory.ToListAsync());
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }

    // The exact live 2026-10-12 case: a human moved the agent to GSD at 05:44, so the
    // accept is refused and the user is told who already holds the day and why.
    [Fact]
    public async Task ManualProtectedEntry_IsRefused_WithTheExactReason()
    {
        var (app, client) = await StartAsync(db =>
        {
            db.ShiftEntries.Add(new ShiftEntry
            {
                Id = 56515, EmployeeId = "9074345", ShiftDate = Day, ShiftType = ShiftTypes.Working,
                ShiftStart = "07:30", ShiftEnd = "16:00", AgentTask = "GSD",
                AutoGenerated = false, SourceModule = "MOVE_TO_GSD",
                ManualChangeBy = "S60028", ManualChangeAt = new DateTime(2026, 10, 12, 3, 44, 14, DateTimeKind.Utc),
                ManualChangeNote = "Manual shift cell edit (/shifts); automatic writers must preserve this row.",
            });
            db.WicShiftEntries.Add(new WicShiftEntry
            {
                Id = 67713, EmployeeId = "9074345", ShiftDate = Day, DayOfWeek = "Monday",
                SupportLocation = "Grafenrheinfeld", WorkingShift = "07:30-16:00",
                IsOnSite = false, IsGSDDay = true, Task = "WIC",
            });
        });
        try
        {
            var (status, body) = await AcceptAsync(client);
            Assert.Equal(HttpStatusCode.Conflict, status);
            Assert.Equal("MANUAL_ENTRY_PROTECTED", Code(body));
            Assert.Contains("Angelika Weber already has a duty today: GSD", Error(body));
            Assert.Contains("protected manual entry", Error(body));
            Assert.Equal("GSD", body.GetProperty("details").GetProperty("duty").GetString());

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            var row = await db.ShiftEntries.SingleAsync(s => s.Id == 56515);
            Assert.Equal(ShiftTypes.Working, row.ShiftType);
            Assert.Equal("GSD", row.AgentTask);
            Assert.Equal("S60028", row.ManualChangeBy);
            var wic = await db.WicShiftEntries.SingleAsync(w => w.Id == 67713);
            Assert.False(wic.IsOnSite);
            Assert.True(wic.IsGSDDay);
            Assert.Empty(await db.SubstitutionHistory.ToListAsync());
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }

    // A second WIC_DUTY row on the same day would collide with the row being changed.
    // The reason names the existing duty instead of SaveChanges throwing.
    [Fact]
    public async Task SecondWicDutyRow_IsRefused_WithTheExactReason()
    {
        var (app, client) = await StartAsync(db =>
        {
            db.ShiftEntries.Add(new ShiftEntry
            {
                Id = 103, EmployeeId = "9074345", ShiftDate = Day, ShiftType = ShiftTypes.Working,
                AgentTask = "Desk", AutoGenerated = true, SourceSheet = "Roster",
            });
            db.ShiftEntries.Add(new ShiftEntry
            {
                Id = 104, EmployeeId = "9074345", ShiftDate = Day, ShiftType = ShiftTypes.WicDuty,
                IsWicDuty = true, AgentTask = "Emmerthal", AutoGenerated = true, SourceSheet = "SUBSTITUTE",
            });
        });
        try
        {
            var (status, body) = await AcceptAsync(client);
            Assert.Equal(HttpStatusCode.Conflict, status);
            Assert.Equal("ALREADY_HAS_WIC_DUTY", Code(body));
            Assert.Contains("already has a WIC duty today", Error(body));
            Assert.Contains("Emmerthal", Error(body));

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            Assert.Equal(2, await db.ShiftEntries.CountAsync());
            Assert.Equal(ShiftTypes.Working, (await db.ShiftEntries.SingleAsync(s => s.Id == 103)).ShiftType);
            Assert.Empty(await db.SubstitutionHistory.ToListAsync());
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
