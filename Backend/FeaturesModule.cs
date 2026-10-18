using Microsoft.Extensions.Options;

namespace GSDDashboard.API;

/// <summary>
/// Feature flags, read through IOptionsMonitor so a change in the configuration
/// (appsettings.json / production appsettings) takes effect WITHOUT a restart.
/// Defaults are false (base appsettings.json); enabling happens only in the
/// production configuration outside the git working tree.
/// </summary>
public sealed class FeaturesOptions
{
    public bool OverviewV2 { get; set; }
    public bool OverviewGroupingV21 { get; set; }
    public bool WhatsNew { get; set; }
    // B4: per-day "3 Urlaub · 1 krank" chips above the weekly Shift Plan.
    public bool AbsenceStrip { get; set; }
    // B7: "Export calendar" (ICS) on My View for the logged-in user's own shifts.
    public bool IcsExport { get; set; }
    public bool RulesEngine { get; set; }
    public bool RuleWarnings { get; set; }
}

public static class FeaturesEndpointMapper
{
    public static void MapFeaturesEndpoints(this WebApplication app)
    {
        // Authorized only — any authenticated session may read flags; the values are
        // non-sensitive UI toggles. Anonymous callers get 401 like /api/auth/me.
        app.MapGet("/api/features", (IOptionsMonitor<FeaturesOptions> monitor, HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Json(new { error = "Authentication required" }, statusCode: StatusCodes.Status401Unauthorized);
            var f = monitor.CurrentValue;
            return Results.Ok(new { overviewV2 = f.OverviewV2, overviewGroupingV21 = f.OverviewGroupingV21, whatsNew = f.WhatsNew, absenceStrip = f.AbsenceStrip, icsExport = f.IcsExport, rulesEngine = f.RulesEngine, ruleWarnings = f.RuleWarnings });
        }).WithTags("Features");
    }
}
