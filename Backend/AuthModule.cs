using System.Security.Claims;
using GSDDashboard.API.Data.Models;
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

/// <summary>
/// Login body. Every field is optional so that an OLD client sending only
/// { "kid": "..." } still binds — that path is what Auth:SupervisorMode governs.
/// </summary>
public record LoginRequest(string? Kid, string? Username = null, string? Password = null,
                           string? Totp = null, string? RecoveryCode = null);

/// <summary>off | shadow | enforce — see documentation/KIOSK_HARDENING_DEPLOY_NOTE.md.</summary>
public static class SupervisorMode
{
    public const string Off = "off";
    public const string Shadow = "shadow";
    public const string Enforce = "enforce";

    public static string Current(IConfiguration cfg) =>
        (cfg["Auth:SupervisorMode"] ?? Shadow).Trim().ToLowerInvariant() switch
        {
            Off => Off,
            Enforce => Enforce,
            _ => Shadow,
        };
}

public static class AuthEndpointMapper
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/auth").WithTags("Auth");

        // Sign-in. Two paths:
        //   (1) supervisor credentials (username + password + TOTP) verified by the
        //       ShiftKiosk account store via ISupervisorAuthProvider;
        //   (2) the LEGACY KID-only path, which is what the security test abused:
        //       /api/attendance on the kiosk server published every agent's
        //       employee_id, and AuthService rule (c) accepts an employee_id as a
        //       complete credential. Auth:SupervisorMode closes that chain.
        grp.MapPost("/login", async Task<IResult> (LoginRequest req, HttpContext ctx,
                                                   AuthService auth,
                                                   ISupervisorAuthProvider supervisor,
                                                   IConfiguration cfg,
                                                   ILoggerFactory lf) =>
        {
            var log  = lf.CreateLogger("Auth");
            var mode = SupervisorMode.Current(cfg);
            var ip   = ctx.Connection.RemoteIpAddress?.ToString();

            // ---- path (1): real credentials -----------------------------------
            if (!string.IsNullOrWhiteSpace(req.Username) && !string.IsNullOrWhiteSpace(req.Password))
            {
                SupervisorIdentity? sup;
                try
                {
                    sup = await supervisor.VerifyAsync(req.Username!, req.Password!,
                                                       req.Totp, req.RecoveryCode);
                }
                catch (SupervisorAuthUnavailableException ex)
                {
                    log.LogError(ex, "Supervisor auth provider unavailable (mode={Mode}, ip={Ip})",
                                 mode, ip);
                    return Results.Json(new { error = "Authentication service unavailable" },
                                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (sup == null)
                {
                    log.LogWarning("Supervisor login failed for {User} from {Ip}", req.Username, ip);
                    return Results.Json(new { error = "Invalid credentials" },
                                        statusCode: StatusCodes.Status401Unauthorized);
                }

                // Only the three roles may hold a Pulse session. Anything else is denied.
                if (sup.Role is not (AppRoles.TeamLead or AppRoles.Rtm or AppRoles.Dev))
                {
                    log.LogWarning("Supervisor {User} has role {Role}, which may not use Pulse",
                                   sup.Username, sup.Role);
                    return Results.Json(new { error = "Invalid credentials" },
                                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var supClaims = new List<Claim>
                {
                    new(AuthClaims.Kid,  sup.Kid ?? sup.Username),
                    new(AuthClaims.Name, sup.DisplayName),
                    new(AuthClaims.Role, sup.Role),
                };
                var supIdentity = new ClaimsIdentity(supClaims, CookieAuthenticationDefaults.AuthenticationScheme);
                await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                                      new ClaimsPrincipal(supIdentity));
                log.LogInformation("Supervisor login OK: {User} ({Role}) from {Ip}",
                                   sup.Username, sup.Role, ip);
                return Results.Ok(new
                {
                    kid = sup.Kid ?? sup.Username,
                    name = sup.DisplayName,
                    role = sup.Role,
                    employeeId = (string?)null,
                    mustChangePassword = sup.MustChangePassword,
                });
            }

            // ---- path (2): legacy KID-only ------------------------------------
            if (mode == SupervisorMode.Enforce)
            {
                log.LogWarning("KID-only login REFUSED (enforce): kid={Kid} ip={Ip}", req.Kid, ip);
                return Results.Json(new { error = "Sign in with your account and authenticator code" },
                                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await auth.ResolveLoginAsync(req.Kid);
            if (result == null)
            {
                // Neutral on purpose: do not reveal whether a KID exists.
                return Results.Json(new { error = "Unknown KID" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (mode == SupervisorMode.Shadow)
            {
                // Nothing is blocked, but every legacy sign-in is recorded so the
                // owner can see who would be locked out before switching to enforce.
                log.LogWarning("SHADOW would-deny: KID-only login accepted for {Kid} ({Role}) from {Ip}. "
                               + "In enforce this request would be refused.",
                               result.Kid, result.Role, ip);
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
