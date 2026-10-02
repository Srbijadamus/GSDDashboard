using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GSDDashboard.API.Services;

/// <summary>
/// Verifies supervisor credentials (password + TOTP) by delegating to the
/// ShiftKiosk server, which OWNS the account store.
///
/// WHY DELEGATE INSTEAD OF RE-IMPLEMENTING
///   The alternative was a shared table that both stacks read and verify
///   independently. That means two implementations of PBKDF2, TOTP, constant-time
///   comparison and lockout — two places to get wrong and two places to patch.
///   One owner, one implementation, one audit trail.
///   COST, stated plainly: Pulse sign-in now depends on the ShiftKiosk service
///   being up. Both run on the same host today and move together. If it is down,
///   set Auth:SupervisorMode=off to fall back to the previous behaviour
///   (see documentation/KIOSK_HARDENING_DEPLOY_NOTE.md, "Break-glass").
///
/// The shared secret below is a SERVICE credential between two server processes.
/// It never reaches a browser and never reaches a kiosk.
/// </summary>
public record SupervisorIdentity(
    [property: JsonPropertyName("username")]      string Username,
    [property: JsonPropertyName("role")]          string Role,
    [property: JsonPropertyName("display_name")]  string DisplayName,
    [property: JsonPropertyName("kid")]           string? Kid,
    [property: JsonPropertyName("team_leader")]   string? TeamLeader,
    [property: JsonPropertyName("must_change_password")] bool MustChangePassword);

public interface ISupervisorAuthProvider
{
    /// <summary>Null when the credentials are not valid. Never throws for a bad password.</summary>
    Task<SupervisorIdentity?> VerifyAsync(string username, string password, string? totp,
                                          string? recoveryCode, CancellationToken ct = default);
    bool IsConfigured { get; }
}

/// <summary>
/// The provider interface is the single seam for a later move to Entra ID,
/// Kerberos or client certificates: implement ISupervisorAuthProvider, register
/// it in Program.cs, and NOTHING in the authorization code changes.
/// </summary>
public sealed class ShiftKioskSupervisorAuthProvider : ISupervisorAuthProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<ShiftKioskSupervisorAuthProvider> _log;
    private readonly string _baseUrl;

    public ShiftKioskSupervisorAuthProvider(HttpClient http, IConfiguration cfg,
                                            ILogger<ShiftKioskSupervisorAuthProvider> log)
    {
        _http = http;
        _log = log;
        _baseUrl = cfg["Auth:SupervisorVerifyUrl"] ?? "http://localhost:8000";
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl);

    public async Task<SupervisorIdentity?> VerifyAsync(string username, string password,
                                                       string? totp, string? recoveryCode,
                                                       CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                username,
                password,
                totp,
                recovery_code = recoveryCode
            };
            var resp = await _http.PostAsJsonAsync($"{_baseUrl.TrimEnd('/')}/api/auth/verify",
                                                   body, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return null;                       // bad credentials — expected path
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Supervisor verify returned {Status}", resp.StatusCode);
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<SupervisorIdentity>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // A transport failure must NOT be reported as "wrong password": it is an
            // outage. The caller decides what to do based on Auth:SupervisorMode.
            _log.LogError(ex, "Supervisor verify call failed against {Base}", _baseUrl);
            throw new SupervisorAuthUnavailableException(ex.Message, ex);
        }
    }
}

public sealed class SupervisorAuthUnavailableException : Exception
{
    public SupervisorAuthUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}
