using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;

namespace GSDDashboard.API.Modules.Auth;

public static class KioskEndpointMapper
{
    public static void MapKioskEndpoints(this WebApplication app)
    {
        app.MapGet("/api/kiosk/attendance", async (string? date, string? tl,
                                                   IKioskProxy proxy, HttpContext ctx,
                                                   CancellationToken ct) =>
        {
            // employee_id is a legacy Pulse credential; this gate must not depend
            // on the Auth:EnforceAuthorization rollout flag.
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Json(new { error = "Authentication required" },
                                    statusCode: StatusCodes.Status401Unauthorized);

            var role = ctx.User.FindFirst(AuthClaims.Role)?.Value;
            if (role is not (AppRoles.Dev or AppRoles.Rtm or AppRoles.TeamLead))
            {
                var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RBAC");
                logger.LogInformation("Kiosk proxy 403: role={Role} {Method} {Path}",
                    role, ctx.Request.Method, ctx.Request.Path);
                return Results.Json(new { error = "Forbidden" },
                                    statusCode: StatusCodes.Status403Forbidden);
            }

            var (status, body) = await proxy.GetAttendanceAsync(date, tl, ct);
            return Results.Content(body, "application/json", statusCode: status);
        }).WithTags("Kiosk");
    }
}
