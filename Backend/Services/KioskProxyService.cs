using System.Net.Http.Headers;

namespace GSDDashboard.API.Services;

/// <summary>
/// Server-side proxy for the ShiftKiosk attendance feed.
///
/// WHY THIS EXISTS
///   Attendance.tsx:75-88 and WicAttendance.tsx:762-776 fetched the kiosk server's
///   /api/attendance DIRECTLY FROM THE BROWSER, with no credential, and both key
///   their maps on `employee_id` (Attendance.tsx:98, WicAttendance.tsx:811, 827,
///   840, 1312). That same `employee_id` is a complete Pulse sign-in under
///   AuthService rule (c), which is the leak the whole hardening exercise is
///   about. Once the kiosk server strips identifiers for unauthenticated callers
///   (flag anon_identifiers=enforce) both pages collapse — proven by
///   tests/test_consumers.py --profile dan3 before this class existed.
///
///   So the browser stops calling the kiosk at all. It calls Pulse, Pulse calls
///   the kiosk with a SERVICE DEVICE TOKEN, and the full payload — including
///   employee_id — is returned only to a caller that already holds a Pulse
///   session. The identifier never reaches an anonymous client again.
///
/// REJECTED ALTERNATIVES
///   * Keep employee_id anonymous — that is the vulnerability.
///   * Return an opaque hashed id — the frontend matches kiosk rows against
///     Pulse's own employeeId, so an opaque id matches nothing.
///
/// The service token is a normal row in the kiosk server's sec_devices table
/// (enroll with: admin_cli.py device enroll --hostname pulse-backend). It is
/// revocable per device like any other, and it never reaches a browser.
/// </summary>
public interface IKioskProxy
{
    Task<(int Status, string Body)> GetAttendanceAsync(string? date, string? tl,
                                                       CancellationToken ct = default);
    bool IsConfigured { get; }
}

public sealed class KioskProxy : IKioskProxy
{
    private readonly HttpClient _http;
    private readonly ILogger<KioskProxy> _log;
    private readonly string _baseUrl;
    private readonly string? _serviceToken;
    private readonly string? _deviceId;

    public KioskProxy(HttpClient http, IConfiguration cfg, ILogger<KioskProxy> log)
    {
        _http = http;
        _log = log;
        _baseUrl = (cfg["Kiosk:BaseUrl"] ?? "http://localhost:8000").TrimEnd('/');
        _serviceToken = cfg["Kiosk:ServiceToken"];
        _deviceId = cfg["Kiosk:ServiceDeviceId"];
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_serviceToken);

    public async Task<(int Status, string Body)> GetAttendanceAsync(string? date, string? tl,
                                                                    CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(date)) qs.Add($"date={Uri.EscapeDataString(date)}");
        if (!string.IsNullOrWhiteSpace(tl)) qs.Add($"tl={Uri.EscapeDataString(tl)}");
        var url = $"{_baseUrl}/api/attendance" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

        // FAIL LOUDLY, NEVER SILENTLY (independent review, finding 2).
        // With an empty ServiceToken this proxy would send no credential, the
        // kiosk would classify it as anonymous, strip employee_id, and both
        // Pulse pages would render "No Check-in" for everyone with HTTP 200 and
        // no error anywhere. A misconfiguration must be obvious.
        if (!IsConfigured)
        {
            _log.LogError("Kiosk:ServiceToken is not configured. Refusing to call the kiosk "
                        + "server anonymously, because the response would silently be missing "
                        + "employee_id and both attendance pages would show everyone as not "
                        + "checked in. Enroll a service device: "
                        + "admin_cli.py device enroll --hostname pulse-backend");
            return (503, "{\"error\":\"Kiosk proxy not configured (Kiosk:ServiceToken)\"}");
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("X-Device-Token", _serviceToken);
        if (!string.IsNullOrWhiteSpace(_deviceId))
            req.Headers.TryAddWithoutValidation("X-Device-Id", _deviceId);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Kiosk attendance proxy: {Status} from {Url}",
                                (int)resp.StatusCode, url);
                return ((int)resp.StatusCode, body);
            }
            // A 200 whose rows have no employee_id means our device token was
            // not accepted (revoked, or the wrong token) and we were served the
            // anonymous payload. Say so instead of handing the pages data that
            // will quietly match nothing.
            if (body.Length > 2 && !body.Contains("\"employee_id\"", StringComparison.Ordinal))
            {
                _log.LogError("Kiosk attendance proxy got a 200 WITHOUT employee_id - the "
                            + "service device token was not accepted (revoked?). Check "
                            + "admin_cli.py device list.");
            }
            return ((int)resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            // The kiosk server being down must not take a Pulse page down with
            // it: both callers already treat an error as "no kiosk data" and
            // render the rest of the page. Return an empty array, not a 500.
            _log.LogError(ex, "Kiosk attendance proxy failed against {Url}", url);
            return (200, "[]");
        }
    }
}
