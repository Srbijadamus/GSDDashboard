using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.BoList;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.SickLeave;
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

namespace GSDDashboard.Tests;

/// <summary>
/// Bulk RTM Entry ("Paste &amp; Parse"), 2026-10-09.
///
/// The paste panel parses one agent name per line (no date, no time) and then sends,
/// per category, exactly the call its own Manual Entry surface sends for a whole day:
///   AL / OFF / CD / NIGHT -> POST /api/shifts/assign/manual   (session KID => manual)
///   SL                    -> POST /api/sickleave              (the SickLeave page's Add)
///   BO                    -> POST /api/bo-list                (the BO Liste page's Add, no time)
///   WIC                   -> POST /api/wic/assignments        (the WIC "Assign to gap")
///
/// These tests prove the stored result of each of those calls on a fresh in-memory
/// database: today, whole day (no time), marked manual, and identical to the Manual
/// Entry write for the same category and day.
/// </summary>
public class BulkRtmPasteTests
{
    private const string Operator = "TEST-OPERATOR";
    private const string WicLocationCode = "ESSEN-TK";
    private const string WicLocationName = "Essen - TK";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private static readonly string TodayIso = Today.ToString("yyyy-MM-dd");

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
        builder.Services.AddDbContext<GSDContext>(options => options.UseInMemoryDatabase(database));
        builder.Services.AddScoped<ShiftService>();
        builder.Services.AddScoped<ShiftValidationService>();
        builder.Services.AddScoped<WicShiftService>();
        builder.Services.AddScoped<AvailabilityResolver>();
        builder.Services.AddScoped<BoListService>();
        builder.Services.AddScoped<SickLeaveService>();
        builder.Services.AddScoped<ShiftSyncService>();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseAuthentication();
        app.MapShiftEndpoints();
        app.MapWicEndpoints();
        app.MapBoListEndpoints();
        app.MapSickLeaveEndpoints();
        app.MapPost("/test/login", async (HttpContext ctx) =>
        {
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(AuthClaims.Kid, Operator), new Claim(AuthClaims.Role, AppRoles.Rtm)],
                    CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok();
        });

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            foreach (var id in new[] { "E-AL", "E-SL", "E-OFF", "E-CD", "E-NIGHT", "E-BO", "E-WIC", "E-MANUAL", "E-LEGACY" })
                db.Employees.Add(new Employee { EmployeeId = id, FullName = $"Synthetic {id}", IsActive = true });
            db.WicLocations.Add(new WicLocation
            {
                LocationCode = WicLocationCode,
                DisplayName = WicLocationName,
                IsActive = true,
            });
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

    /// <summary>The exact body the paste panel builds for AL / OFF / CD / NIGHT.</summary>
    private static object PasteShiftBody(string employeeId, string shiftType) => new
    {
        employeeId,
        shiftDate = TodayIso,
        shiftType,
        shiftStart = (string?)null,
        shiftEnd = (string?)null,
    };

    private static async Task<ShiftEntry> RowAsync(WebApplication app, string employeeId, DateOnly? date = null)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
        return await db.ShiftEntries.SingleAsync(s => s.EmployeeId == employeeId && s.ShiftDate == (date ?? Today));
    }

    // ── Every category, name only → today, whole day, no time, manual ──────────

    [Theory]
    [InlineData("AL")]
    [InlineData("OFF")]
    [InlineData("CD")]
    public async Task Paste_ShiftCategories_WriteTodayWholeDay_MarkedManual(string shiftType)
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var login = await client.PostAsync("/test/login", null);

            using var res = await client.PostAsJsonAsync("/api/shifts/assign/manual", PasteShiftBody("E-" + shiftType, shiftType));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var row = await RowAsync(app, "E-" + shiftType);
            Assert.Equal(shiftType, row.ShiftType);
            Assert.Null(row.ShiftStart);                    // whole day — no time stored
            Assert.Null(row.ShiftEnd);
            Assert.False(row.AutoGenerated);
            Assert.Equal(Operator, row.ManualChangeBy);     // marked manual
            Assert.NotNull(row.ManualChangeAt);
            Assert.True(row.IsManualChange);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Paste_Night_IsAWorkingShift_ForTodayWholeDay_MarkedManual()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var login = await client.PostAsync("/test/login", null);

            using var res = await client.PostAsJsonAsync("/api/shifts/assign/manual", PasteShiftBody("E-NIGHT", "WORKING"));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var row = await RowAsync(app, "E-NIGHT");
            Assert.Equal("WORKING", row.ShiftType);
            Assert.Null(row.ShiftStart);
            Assert.Null(row.ShiftEnd);
            Assert.Equal(Operator, row.ManualChangeBy);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Paste_SickLeave_WritesTheSickLeavePageRecord_ForToday()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var login = await client.PostAsync("/test/login", null);

            // Same body the paste panel builds for SL (open-ended end date as the SickLeave page does).
            using var res = await client.PostAsJsonAsync("/api/sickleave", new
            {
                employeeId = "E-SL",
                startDate = TodayIso,
                endDate = "2099-12-31",
                type = "Self",
                notes = (string?)null,
            });
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            var record = await db.SickLeaves.SingleAsync(s => s.EmployeeId == "E-SL");
            Assert.Equal(Today, record.FirstDay);
            Assert.Equal(new DateOnly(2099, 12, 31), record.LastDay);
            Assert.Equal("Self", record.LeaveType);

            // The sync writes the SL day itself (weekdays only — the SickLeave record
            // above is always today, which is what "applies to today" requires).
            if (Today.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                var shift = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E-SL" && s.ShiftDate == Today);
                Assert.Equal("SL", shift.ShiftType);
                Assert.Null(shift.ShiftStart);
                Assert.Null(shift.ShiftEnd);
            }
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Paste_BoList_WritesATimeLessEntryForToday()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var login = await client.PostAsync("/test/login", null);

            // The paste panel replaces today's BO list, then adds one name-only row.
            using var clear = await client.DeleteAsync($"/api/bo-list/by-date?date={TodayIso}");
            Assert.Equal(HttpStatusCode.OK, clear.StatusCode);

            using var res = await client.PostAsJsonAsync("/api/bo-list", new
            {
                date = TodayIso,
                employeeName = "Synthetic E-BO",
                shiftStart = "",
                shiftEnd = "",
                note = (string?)null,
            });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            var entry = await db.BoEntries.SingleAsync(b => b.EntryDate == Today);
            Assert.Equal("Synthetic E-BO", entry.EmployeeName);
            Assert.Equal(string.Empty, entry.ShiftStart);   // whole day — no time stored
            Assert.Equal(string.Empty, entry.ShiftEnd);
            Assert.Null(entry.Note);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Paste_Wic_WritesTheAssignmentForToday_WithTheCentresHours_AsAManualChange()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var login = await client.PostAsync("/test/login", null);

            using var res = await client.PostAsJsonAsync("/api/wic/assignments", new
            {
                employeeId = "E-WIC",
                locationCode = WicLocationCode,
                date = TodayIso,
                shiftStart = (string?)null,
                shiftEnd = (string?)null,
            });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            var wic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "E-WIC");
            Assert.Equal(Today, wic.ShiftDate);
            Assert.True(wic.IsOnSite);
            Assert.Equal(WicLocationName, wic.SupportLocation);
            Assert.Equal("WIC", wic.Task);

            var shift = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "E-WIC" && s.ShiftDate == Today);
            Assert.Equal("WIC_DUTY", shift.ShiftType);
            Assert.True(shift.IsWicDuty);
            Assert.Equal(Operator, shift.ManualChangeBy);
            // The window comes from the centre's own hours, never from the paste.
            Assert.Equal("08:00", shift.ShiftStart);
            Assert.Equal("17:00", shift.ShiftEnd);
        }
        finally { await app.StopAsync(); }
    }

    // ── Manual Entry for the same category and day writes the same thing ───────

    [Theory]
    [InlineData("AL")]
    [InlineData("OFF")]
    [InlineData("CD")]
    [InlineData("WORKING")]
    public async Task PasteWrite_EqualsTheShiftsPageManualEntry_ForTheSameCategoryAndDay(string shiftType)
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var login = await client.PostAsync("/test/login", null);

            // The paste row (AL / OFF / CD / NIGHT).
            using var paste = await client.PostAsJsonAsync("/api/shifts/assign/manual", PasteShiftBody("E-AL", shiftType));
            Assert.Equal(HttpStatusCode.OK, paste.StatusCode);

            // The Shifts page creating a new cell of the same type by hand — same route,
            // same body shape, no time.
            using var manual = await client.PostAsJsonAsync("/api/shifts/assign/manual", PasteShiftBody("E-MANUAL", shiftType));
            Assert.Equal(HttpStatusCode.OK, manual.StatusCode);

            var pasteRow = await RowAsync(app, "E-AL");
            var manualRow = await RowAsync(app, "E-MANUAL");

            Assert.Equal(manualRow.ShiftType, pasteRow.ShiftType);
            Assert.Equal(manualRow.ShiftStart, pasteRow.ShiftStart);
            Assert.Equal(manualRow.ShiftEnd, pasteRow.ShiftEnd);
            Assert.Equal(manualRow.AutoGenerated, pasteRow.AutoGenerated);
            Assert.Equal(manualRow.ManualChangeBy, pasteRow.ManualChangeBy);
            Assert.Equal(manualRow.ManualChangeNote, pasteRow.ManualChangeNote);
            Assert.True(pasteRow.IsManualChange);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task PastedRow_IsProtectedFromTheActorlessAutomaticBulkWriter()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var login = await client.PostAsync("/test/login", null);

            using var paste = await client.PostAsJsonAsync("/api/shifts/assign/manual", PasteShiftBody("E-LEGACY", "OFF"));
            Assert.Equal(HttpStatusCode.OK, paste.StatusCode);

            // /api/shifts/assign/bulk has no session actor: an automatic writer. It must
            // refuse to overwrite the paste row a human just made.
            using var automatic = await client.PostAsJsonAsync("/api/shifts/assign/bulk", new
            {
                employeeId = "E-LEGACY",
                shiftDate = TodayIso,
                shiftType = "WORKING",
                shiftStart = "09:00",
                shiftEnd = "17:00",
            });
            Assert.Equal(HttpStatusCode.Conflict, automatic.StatusCode);

            var row = await RowAsync(app, "E-LEGACY");
            Assert.Equal("OFF", row.ShiftType);
            Assert.Null(row.ShiftStart);
            Assert.Equal(Operator, row.ManualChangeBy);
        }
        finally { await app.StopAsync(); }
    }

    // ── The day is taken automatically from the server's local date ────────────

    [Fact]
    public async Task PasteWithoutADate_LandsOnTheServersLocalToday()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var login = await client.PostAsync("/test/login", null);

            using var res = await client.PostAsJsonAsync("/api/shifts/assign/manual", new
            {
                employeeId = "E-OFF",
                shiftDate = "",
                shiftType = "OFF",
                shiftStart = (string?)null,
                shiftEnd = (string?)null,
            });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var row = await RowAsync(app, "E-OFF");
            Assert.Equal(DateOnly.FromDateTime(DateTime.Today), row.ShiftDate);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task AnUnparsableDateStillFailsClosed()
    {
        await using var app = await BuildAppAsync(Guid.NewGuid().ToString());
        try
        {
            using var client = ClientFor(app);
            using var login = await client.PostAsync("/test/login", null);

            using var res = await client.PostAsJsonAsync("/api/shifts/assign/manual", new
            {
                employeeId = "E-CD",
                shiftDate = "not-a-date",
                shiftType = "CD",
            });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
        finally { await app.StopAsync(); }
    }
}
