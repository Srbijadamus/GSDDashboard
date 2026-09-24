using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;

namespace GSDDashboard.API.Middleware;

/// <summary>
/// Role-based enforcement for /api/*, SERVER SIDE (hiding buttons in React is not
/// access control). Default DENY: anything not explicitly allowed is rejected.
///
/// Gate: configuration flag "Auth:EnforceAuthorization".
///   false → middleware is a no-op (rollout phase: login works, nothing blocked yet).
///   true  → rules below apply. Flip only AFTER a DEV login was proven over both the
///           https devtunnel URL and http://localhost:5000 (see documentation/auth_rbac.md).
///
/// Rules (when enabled):
///   - Non-/api paths (static files, /health, SPA fallback): untouched.
///   - Anonymous allow-list: /api/auth/login, /api/debug/client-error (both rate-limited).
///   - Unauthenticated /api request → 401 { error: "Authentication required" }.
///   - DEV → unrestricted, bypasses everything below.
///   - /api/admin/* (cleanup-wic-orphans, demo-data) → DEV only, 403 otherwise.
///   - TEAM_LEAD / RTM → unrestricted read/write on the functional endpoints.
///   - AGENT → GET allow-list scoped to their OWN EmployeeId (see AgentAllowed);
///     every write (non-GET/HEAD) → 403. Zero write access anywhere.
/// </summary>
public static class RbacMiddleware
{
    public static IApplicationBuilder UseRbac(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
            if (!config.GetValue<bool>("Auth:EnforceAuthorization"))
            {
                await next();
                return;
            }

            var path = ctx.Request.Path;
            if (!path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            // Anonymous allow-list (both endpoints are rate-limited via the "auth" policy).
            if (path.StartsWithSegments("/api/auth/login") ||
                path.StartsWithSegments("/api/debug/client-error"))
            {
                await next();
                return;
            }

            if (ctx.User.Identity?.IsAuthenticated != true)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { error = "Authentication required" });
                return;
            }

            var role = ctx.User.FindFirst(AuthClaims.Role)?.Value ?? "";

            // DEV bypasses all policies — never restricted, never locked out.
            if (role == AppRoles.Dev)
            {
                await next();
                return;
            }

            // Admin surface is DEV-only.
            if (path.StartsWithSegments("/api/admin"))
            {
                await Forbidden(ctx, role);
                return;
            }

            // TEAM_LEAD and RTM: unrestricted read and write on functional endpoints.
            if (role is AppRoles.TeamLead or AppRoles.Rtm)
            {
                await next();
                return;
            }

            if (role == AppRoles.Agent && AgentAllowed(ctx))
            {
                await next();
                return;
            }

            await Forbidden(ctx, role);
        });

    private static async Task Forbidden(HttpContext ctx, string role)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { error = "Forbidden" });
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RBAC");
        logger.LogInformation("RBAC 403: role={Role} {Method} {Path}",
            role, ctx.Request.Method, ctx.Request.Path);
    }


    /// <summary>
    /// AGENT read allow-list. Everything here is scoped to the agent's own EmployeeId
    /// (or own KID); an AGENT must not be able to read another agent's row by changing
    /// an id in the URL or a query parameter. Anything not listed → 403.
    /// </summary>
    private static bool AgentAllowed(HttpContext ctx)
    {
        var path   = ctx.Request.Path.Value ?? "";
        var ownId  = ctx.User.FindFirst(AuthClaims.EmployeeId)?.Value ?? "";
        var ownKid = ctx.User.FindFirst(AuthClaims.Kid)?.Value ?? "";

        // Session endpoints are allowed for every authenticated role.
        if (path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
            return true;

        // AGENT: zero write access anywhere.
        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
            return false;

        // An AGENT session must always carry a real employeeId (startup-enforced).
        if (ownId.Length == 0)
            return false;

        // Own employee record, own timeline (shifts + sick leave + vacations), own future-shift count.
        if (path.Equals($"/api/employees/{ownId}", StringComparison.OrdinalIgnoreCase) ||
            path.Equals($"/api/employees/{ownId}/timeline", StringComparison.OrdinalIgnoreCase) ||
            path.Equals($"/api/employees/{ownId}/future-shifts", StringComparison.OrdinalIgnoreCase))
            return true;

        // Own AL balance.
        if (path.Equals($"/api/albalance/{ownId}", StringComparison.OrdinalIgnoreCase))
            return true;

        // List endpoints that support an employeeId filter — only with own id, never
        // unfiltered and never with somebody else's id.
        if (path.Equals("/api/vacations", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/vacations/", StringComparison.OrdinalIgnoreCase))
            return string.Equals(ctx.Request.Query["employeeId"], ownId, StringComparison.OrdinalIgnoreCase);

        if (path.Equals("/api/wic", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/wic/", StringComparison.OrdinalIgnoreCase))
            return string.Equals(ctx.Request.Query["employeeId"], ownId, StringComparison.OrdinalIgnoreCase);

        // Own WIC coverage detail (endpoint accepts KID or employee id).
        const string coveragePrefix = "/api/wic-coverage/agents/";
        if (path.StartsWith(coveragePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var seg = path[coveragePrefix.Length..];
            return seg.Equals(ownKid, StringComparison.OrdinalIgnoreCase) ||
                   seg.Equals(ownId, StringComparison.OrdinalIgnoreCase);
        }

        // Non-personal reference data.
        if (path.Equals("/api/wic/locations", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/publicholidays", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
