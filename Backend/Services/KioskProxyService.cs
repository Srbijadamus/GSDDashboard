using System.Net.Http.Headers;
using System.Text.Json;

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
    Task<(int Status, string Body)> GetAgentsAsync(CancellationToken ct = default);
    Task<(int Status, string Body)> ManualActionAsync(bool checkin, int agentId,
        string performedBy, CancellationToken ct = default);
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
        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri) ||
            !uri.IsLoopback || uri.Scheme != "http")
            throw new InvalidOperationException("Kiosk:BaseUrl must be an HTTP loopback URL.");
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
        return await SendAsync(HttpMethod.Get, "/api/attendance" +
            (qs.Count > 0 ? "?" + string.Join("&", qs) : ""), null, "employee_id", ct);
    }

    public Task<(int Status, string Body)> GetAgentsAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, "/api/agents", null, "id", ct);

    public Task<(int Status, string Body)> ManualActionAsync(bool checkin, int agentId,
        string performedBy, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, checkin ? "/api/manual-checkin" : "/api/manual-logout",
            new { agent_id = agentId, performed_by = performedBy, note = "" }, null, ct);

    private async Task<(int Status, string Body)> SendAsync(HttpMethod method, string path,
        object? payload, string? arrayIdentifier, CancellationToken ct)
    {
        var url = $"{_baseUrl}{path}";

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

        using var req = new HttpRequestMessage(method, url);
        if (payload is not null)
            req.Content = JsonContent.Create(payload);
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
            using var document = JsonDocument.Parse(body);
            if (arrayIdentifier is not null && (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.EnumerateArray().Any(row =>
                    row.ValueKind != JsonValueKind.Object ||
                    !row.TryGetProperty(arrayIdentifier, out _))))
            {
                _log.LogError("Kiosk attendance proxy returned an invalid feed or rows without employee_id");
                return (502, "{\"error\":\"Invalid kiosk attendance response\"}");
            }
            if (arrayIdentifier is null &&
                (document.RootElement.ValueKind != JsonValueKind.Object ||
                 !document.RootElement.TryGetProperty("success", out var success) ||
                 success.ValueKind != JsonValueKind.True))
                return (502, "{\"error\":\"Invalid kiosk action response\"}");
            return ((int)resp.StatusCode, body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _log.LogError(ex, "Kiosk attendance proxy timed out");
            return (504, "{\"error\":\"Kiosk attendance request timed out\"}");
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "Kiosk attendance proxy is unavailable");
            return (503, "{\"error\":\"Kiosk attendance service unavailable\"}");
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Kiosk attendance proxy returned malformed JSON");
            return (502, "{\"error\":\"Invalid kiosk attendance response\"}");
        }
    }
}
