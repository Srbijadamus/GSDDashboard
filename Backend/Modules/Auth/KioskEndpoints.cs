using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;

namespace GSDDashboard.API.Modules.Auth;

public static class KioskEndpointMapper
{
    public static void MapKioskEndpoints(this WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/api/kiosk"))
            {
                await next();
                return;
            }
            var denied = CheckAccess(ctx);
            if (denied is not null)
            {
                await denied.ExecuteAsync(ctx);
                return;
            }
            await next();
        });

        app.MapGet("/api/kiosk/attendance", async (string? date, string? tl,
                                                   IKioskProxy proxy, HttpContext ctx,
                                                   CancellationToken ct) =>
        {
            var (status, body) = await proxy.GetAttendanceAsync(date, tl, ct);
            return Results.Content(body, "application/json", statusCode: status);
        }).WithTags("Kiosk");
        app.MapGet("/api/kiosk/agents", async (IKioskProxy proxy, CancellationToken ct) =>
        {
            var (status, body) = await proxy.GetAgentsAsync(ct);
            return Results.Content(body, "application/json", statusCode: status);
        }).WithTags("Kiosk");
        foreach (var checkin in new[] { true, false })
        {
            app.MapPost(checkin ? "/api/kiosk/manual-checkin" : "/api/kiosk/manual-logout",
                async (ManualKioskAction action, IKioskProxy proxy, HttpContext ctx, CancellationToken ct) =>
                {
                    var origin = ctx.Request.Headers.Origin.ToString();
                    if (ctx.Request.Headers["X-Pulse-Request"] != "1" ||
                        !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                        !string.Equals(uri.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
                        return Results.Json(new { error = "Same-origin request required" }, statusCode: 403);
                    if (action.AgentId <= 0)
                        return Results.BadRequest(new { error = "A positive agentId is required" });
                    var actor = ctx.User.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(actor))
                        return Results.Json(new { error = "Session actor required" }, statusCode: 403);
                    var (status, body) = await proxy.ManualActionAsync(checkin, action.AgentId, actor, ct);
                    ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("KioskAudit")
                        .LogInformation("Manual kiosk action actor={Actor} agent={AgentId} checkin={Checkin} status={Status}",
                            actor, action.AgentId, checkin, status);
                    return Results.Content(body, "application/json", statusCode: status);
                }).WithTags("Kiosk");
        }
    }

    private static IResult? CheckAccess(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
            return Results.Json(new { error = "Authentication required" }, statusCode: 401);
        var role = ctx.User.FindFirst(AuthClaims.Role)?.Value;
        if (role is AppRoles.Dev or AppRoles.Rtm or AppRoles.TeamLead)
            return null;
        ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RBAC")
            .LogInformation("Kiosk proxy 403: role={Role} {Method} {Path}", role, ctx.Request.Method, ctx.Request.Path);
        return Results.Json(new { error = "Forbidden" }, statusCode: 403);
    }
}

public sealed record ManualKioskAction(int AgentId);
