using System.Security.Claims;
using GSDDashboard.API.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace GSDDashboard.API.Modules.Auth;

public static class AuthClaims
{
    public const string Kid        = ClaimTypes.NameIdentifier;
    public const string Name       = ClaimTypes.Name;
    public const string Role       = ClaimTypes.Role;
    public const string EmployeeId = "employeeId";
}

public static class RateLimitPolicies
{
    /// <summary>Per-IP fixed-window limit (10/min) for anonymous, abuse-prone endpoints.</summary>
    public const string Auth = "auth";
}

public record LoginRequest(string? Kid);

public static class AuthEndpointMapper
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/auth").WithTags("Auth");

        // KID-only login, no password. Rate-limited per IP — with no second factor,
        // this is the only barrier between a guessed KID and the data.
        grp.MapPost("/login", async Task<IResult> (LoginRequest req, HttpContext ctx, AuthService auth) =>
        {
            var result = await auth.ResolveLoginAsync(req.Kid);
            if (result == null)
            {
                // Neutral on purpose: do not reveal whether a KID exists.
                return Results.Json(new { error = "Unknown KID" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var claims = new List<Claim>
            {
                new(AuthClaims.Kid,  result.Kid),
                new(AuthClaims.Name, result.DisplayName),
                new(AuthClaims.Role, result.Role),
            };
            if (!string.IsNullOrEmpty(result.EmployeeId))
                claims.Add(new Claim(AuthClaims.EmployeeId, result.EmployeeId));

            var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Ok(new
            {
                kid        = result.Kid,
                name       = result.DisplayName,
                role       = result.Role,
                employeeId = result.EmployeeId,
            });
        }).RequireRateLimiting(RateLimitPolicies.Auth);

        grp.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { loggedOut = true });
        });

        grp.MapGet("/me", (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Json(new { error = "Not authenticated" }, statusCode: StatusCodes.Status401Unauthorized);

            return Results.Ok(new
            {
                kid        = ctx.User.FindFirst(AuthClaims.Kid)?.Value,
                name       = ctx.User.FindFirst(AuthClaims.Name)?.Value,
                role       = ctx.User.FindFirst(AuthClaims.Role)?.Value,
                employeeId = ctx.User.FindFirst(AuthClaims.EmployeeId)?.Value,
            });
        });
    }
}
