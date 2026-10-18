using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GSDDashboard.API;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.Rules;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SickLeaveModel = GSDDashboard.API.Data.Models.SickLeave;

namespace GSDDashboard.Tests;

public class RulesEngineTests
{
    private static readonly RulesOptions Opts = new() { MinRestHours = 11, MaxConsecutiveDays = 6 };

    private static ShiftEntry Work(string emp, int year, int month, int day, string start, string end) =>
        new() { EmployeeId = emp, ShiftDate = new DateOnly(year, month, day), ShiftType = ShiftTypes.Working, ShiftStart = start, ShiftEnd = end };

    private static SickLeaveModel Sick(string emp, DateOnly first, DateOnly last) =>
        new() { EmployeeId = emp, FirstDay = first, LastDay = last };

    private static Vacation Vac(string emp, DateOnly first, DateOnly last, string? approved = null) =>
        new() { EmployeeId = emp, FirstDay = first, LastDay = last, ApprovedDenied = approved };

    private static List<RuleWarning> Eval(
        IEnumerable<ShiftEntry>? shifts = null,
        IEnumerable<WicShiftEntry>? wic = null,
        IEnumerable<SickLeaveModel>? sick = null,
        IEnumerable<Vacation>? vac = null,
        IEnumerable<TrainingSession>? trainings = null,
        ProposedShift? proposed = null,
        string emp = "E1") =>
        RuleEvaluator.Evaluate(emp,
            shifts ?? Array.Empty<ShiftEntry>(),
            wic ?? Array.Empty<WicShiftEntry>(),
            sick ?? Array.Empty<SickLeaveModel>(),
            vac ?? Array.Empty<Vacation>(),
            trainings ?? Array.Empty<TrainingSession>(),
            Opts, proposed);

    // ── R1 overlap ────────────────────────────────────────────────────────────────
    [Fact]
    public void R1_SameDayOverlap_Warns()
    {
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "08:00", "17:00"), Work("E1", 2026, 11, 5, "09:00", "18:00") });
        Assert.Contains(w, x => x.Code == RuleCodes.Overlap && x.Severity == RuleSeverity.Error);
    }

    [Fact]
    public void R1_Touching_NoWarning()
    {
        // EndA == StartB is a touch, not an overlap.
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "08:00", "17:00"), Work("E1", 2026, 11, 5, "17:00", "18:00") });
        Assert.DoesNotContain(w, x => x.Code == RuleCodes.Overlap);
    }

    [Fact]
    public void R1_OverMidnight_Warns()
    {
        // Night 22:00–06:00 (crosses midnight into 11-06) overlaps a 05:00–14:00 on 11-06.
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "22:00", "06:00"), Work("E1", 2026, 11, 6, "05:00", "14:00") });
        Assert.Contains(w, x => x.Code == RuleCodes.Overlap);
    }

    // ── R5 rest ────────────────────────────────────────────────────────────────────
    [Fact]
    public void R5_RestBelowMin_Warns()
    {
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "08:00", "17:00"), Work("E1", 2026, 11, 5, "21:00", "23:00") });
        // 17:00 → 21:00 = 4h rest.
        Assert.Contains(w, x => x.Code == RuleCodes.MinRest);
    }

    [Fact]
    public void R5_RestExactlyMin_NoWarning()
    {
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "08:00", "17:00"), Work("E1", 2026, 11, 6, "04:00", "12:00") });
        // 17:00 → 04:00 = 11h rest (boundary).
        Assert.DoesNotContain(w, x => x.Code == RuleCodes.MinRest);
    }

    // ── R6 consecutive days ────────────────────────────────────────────────────────
    [Fact]
    public void R6_SevenConsecutiveDays_Warns()
    {
        var shifts = Enumerable.Range(0, 7)
            .Select(i => Work("E1", 2026, 11, 2 + i, "08:00", "17:00")).ToArray();
        var w = Eval(shifts);
        Assert.Contains(w, x => x.Code == RuleCodes.Consecutive && (int)x.Params["days"]! == 7);
    }

    [Fact]
    public void R6_SixConsecutiveDays_NoWarning()
    {
        var shifts = Enumerable.Range(0, 6)
            .Select(i => Work("E1", 2026, 11, 2 + i, "08:00", "17:00")).ToArray();
        var w = Eval(shifts);
        Assert.DoesNotContain(w, x => x.Code == RuleCodes.Consecutive);
    }

    // ── R2 absence ─────────────────────────────────────────────────────────────────
    [Fact]
    public void R2_SickLeaveCovering_Warns()
    {
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "08:00", "17:00") },
            sick: new[] { Sick("E1", new(2026, 11, 4), new(2026, 11, 6)) });
        Assert.Contains(w, x => x.Code == RuleCodes.Absence && (string)x.Params["type"]! == "SL");
    }

    [Fact]
    public void R2_VacationFullDay_Warns()
    {
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "08:00", "17:00") },
            vac: new[] { Vac("E1", new(2026, 11, 5), new(2026, 11, 5)) });
        Assert.Contains(w, x => x.Code == RuleCodes.Absence && (string)x.Params["type"]! == "AL");
    }

    [Fact]
    public void R2_VacationDenied_NoWarning()
    {
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "08:00", "17:00") },
            vac: new[] { Vac("E1", new(2026, 11, 5), new(2026, 11, 5), "Denied") });
        Assert.DoesNotContain(w, x => x.Code == RuleCodes.Absence);
    }

    [Fact]
    public void R2_HalfAL_IsNotWorking_NoWarning()
    {
        // HALF_AL is half leave + half work: not a WORKING/WIC_DUTY shift, so a full-absence
        // rule must not fire on it, and no time rule applies.
        var half = new ShiftEntry { EmployeeId = "E1", ShiftDate = new(2026, 11, 5), ShiftType = ShiftTypes.HalfAL };
        var w = Eval(new[] { half });
        Assert.DoesNotContain(w, x => x.Code == RuleCodes.Absence);
    }

    // ── R3 other WIC ────────────────────────────────────────────────────────────────
    [Fact]
    public void R3_TwoWicCentresSameDay_Warns()
    {
        var wic = new[]
        {
            new WicShiftEntry { EmployeeId = "E1", ShiftDate = new(2026, 11, 5), SupportLocation = "A", IsOnSite = true },
            new WicShiftEntry { EmployeeId = "E1", ShiftDate = new(2026, 11, 5), SupportLocation = "B", IsOnSite = true },
        };
        var w = Eval(wic: wic);
        Assert.Contains(w, x => x.Code == RuleCodes.OtherWic && (int)x.Params["locations"]! == 2);
    }

    [Fact]
    public void R3_SingleWicCentre_NoWarning()
    {
        var wic = new[]
        {
            new WicShiftEntry { EmployeeId = "E1", ShiftDate = new(2026, 11, 5), SupportLocation = "A", IsOnSite = true },
            new WicShiftEntry { EmployeeId = "E1", ShiftDate = new(2026, 11, 5), SupportLocation = "A", IsOnSite = true },
        };
        var w = Eval(wic: wic);
        Assert.DoesNotContain(w, x => x.Code == RuleCodes.OtherWic);
    }

    // ── R4 training ────────────────────────────────────────────────────────────────
    [Fact]
    public void R4_TrainingSession_Warns()
    {
        var trainings = new[]
        {
            new TrainingSession { ScheduledDate = new(2026, 11, 5), AgentIds = "E1,E2", StartTime = "09:00", EndTime = "12:00" },
        };
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "08:00", "17:00") }, trainings: trainings);
        Assert.Contains(w, x => x.Code == RuleCodes.Training);
    }

    [Fact]
    public void R4_TrainingOtherAgent_NoWarning()
    {
        var trainings = new[]
        {
            new TrainingSession { ScheduledDate = new(2026, 11, 5), AgentIds = "E2", StartTime = "09:00", EndTime = "12:00" },
        };
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "08:00", "17:00") }, trainings: trainings);
        Assert.DoesNotContain(w, x => x.Code == RuleCodes.Training);
    }

    // ── DATA_INVALID ───────────────────────────────────────────────────────────────
    [Fact]
    public void DataInvalid_UnparseableTime_Warns_NotThrow()
    {
        var w = Eval(new[] { Work("E1", 2026, 11, 5, "25:00", "17:00") });
        Assert.Contains(w, x => x.Code == RuleCodes.DataInvalid && (string)x.Params["field"]! == "start");
    }

    // ── proposed shift ──────────────────────────────────────────────────────────────
    [Fact]
    public void ProposedShift_OverlapsExisting_Warns()
    {
        var existing = new[] { Work("E1", 2026, 11, 5, "08:00", "17:00") };
        var proposed = new ProposedShift("E1", "2026-11-05", "WORKING", "09:00", "18:00");
        var w = Eval(existing, proposed: proposed);
        Assert.Contains(w, x => x.Code == RuleCodes.Overlap);
    }
}

/// <summary>RBAC + feature-flag gate for the new Rules endpoints.</summary>
public class RulesEndpointTests
{
    private static WebApplication BuildApp(bool rulesEngine, Action<GSDContext>? seed = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:RulesEngine"] = rulesEngine ? "true" : "false",
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o => o.Cookie.Name = "gsd_auth");
        builder.Services.AddAuthorization();
        builder.Services.AddDbContext<GSDContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        builder.Services.Configure<FeaturesOptions>(builder.Configuration.GetSection("Features"));
        builder.Services.Configure<RulesOptions>(o => { o.MinRestHours = 11; o.MaxConsecutiveDays = 6; });
        builder.Services.AddScoped<RulesService>();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:5060");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRulesEndpoints();
        app.MapPost("/test/sign-in/{role}", async (string role, HttpContext ctx) =>
        {
            var claims = new List<Claim> { new(AuthClaims.Name, "t"), new(AuthClaims.Role, role) };
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok();
        });

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            seed?.Invoke(db);
        }
        return app;
    }

    private static async Task<(HttpStatusCode anon, HttpStatusCode agent, HttpStatusCode dev)> ProbeAsync(
        WebApplication app, string path, string method, object? body = null)
    {
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { BaseAddress = new Uri("http://127.0.0.1:5060") };

            HttpResponseMessage Anon() => method == "POST"
                ? client.PostAsJsonAsync(path, body).Result
                : client.GetAsync(path).Result;

            var anon = Anon();

            await client.PostAsync("/test/sign-in/AGENT", null);
            var agent = Anon();

            await client.PostAsync("/test/sign-in/DEV", null);
            var dev = Anon();

            return (anon.StatusCode, agent.StatusCode, dev.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task RulesValidate_Anonymous401_Agent403_Dev200()
    {
        await using var app = BuildApp(rulesEngine: true);
        var (anon, agent, dev) = await ProbeAsync(app, "/api/rules/validate", "POST",
            new RulesValidateRequest("2026-11-02", "2026-11-08", null, null));
        Assert.Equal(HttpStatusCode.Unauthorized, anon);
        Assert.Equal(HttpStatusCode.Forbidden, agent);
        Assert.Equal(HttpStatusCode.OK, dev);
    }

    [Fact]
    public async Task RulesWeek_Anonymous401_Agent403_Dev200()
    {
        await using var app = BuildApp(rulesEngine: true);
        var (anon, agent, dev) = await ProbeAsync(app, "/api/rules/week?from=2026-11-02&to=2026-11-08", "GET");
        Assert.Equal(HttpStatusCode.Unauthorized, anon);
        Assert.Equal(HttpStatusCode.Forbidden, agent);
        Assert.Equal(HttpStatusCode.OK, dev);
    }

    [Fact]
    public async Task RulesWeek_FlagOff_Dev200_EmptyResult()
    {
        await using var app = BuildApp(rulesEngine: false);
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { BaseAddress = new Uri("http://127.0.0.1:5060") };
            await client.PostAsync("/test/sign-in/DEV", null);
            using var res = await client.GetAsync("/api/rules/week?from=2026-11-02&to=2026-11-08");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadFromJsonAsync<RulesResponse>();
            Assert.NotNull(body);
            Assert.Empty(body!.People);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
