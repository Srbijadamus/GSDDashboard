using System.Net;
using GSDDashboard.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GSDDashboard.Tests;

public class KioskProxyResponseTests
{
    [Fact]
    public async Task AgentsAndManualActions_UseDeviceCredentialAndFixedPaths()
    {
        using var http = new HttpClient(new ActionHandler());
        var proxy = Proxy(http);
        Assert.Equal(200, (await proxy.GetAgentsAsync()).Status);
        Assert.Equal(200, (await proxy.ManualActionAsync(true, 258, "signed-in-actor")).Status);
        Assert.Equal(200, (await proxy.ManualActionAsync(false, 258, "signed-in-actor")).Status);
    }

    private sealed class ActionHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("127.0.0.1", request.RequestUri?.Host);
            Assert.Equal("TEST-ONLY", request.Headers.GetValues("X-Device-Token").Single());
            Assert.False(request.Headers.Contains("X-API-Key"));
            var body = "[{\"id\":258}]";
            if (request.Method == HttpMethod.Post)
            {
                Assert.Contains(request.RequestUri?.AbsolutePath,
                    new[] { "/api/manual-checkin", "/api/manual-logout" });
                var json = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var document = System.Text.Json.JsonDocument.Parse(json);
                Assert.Equal(258, document.RootElement.GetProperty("agent_id").GetInt32());
                Assert.Equal("signed-in-actor", document.RootElement.GetProperty("performed_by").GetString());
                body = "{\"success\":true}";
            }
            else
                Assert.Equal("/api/agents", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public async Task Feed_VirtualAgentPreserved_OutagesAndInvalidFeedsAreNotEmptySuccess()
    {
        const string virtualAgent =
            "[{\"employee_id\":\"TEST-ONLY\",\"attendance_status\":\"ACTIVE\",\"work_location\":\"HOME_OFFICE\"}]";
        foreach (var (body, status) in new[]
        {
            (virtualAgent, 200), ("[]", 200),
            ("[{\"attendance_status\":\"ACTIVE\"}]", 502), ("{}", 502), ("not-json", 502)
        })
        {
            using var http = new HttpClient(new StubHandler(body));
            var result = await Proxy(http).GetAttendanceAsync(null, null);
            Assert.Equal(status, result.Status);
            if (status == 200) Assert.Equal(body, result.Body);
        }
        foreach (var (failure, status) in new (Exception, int)[]
        {
            (new HttpRequestException("test outage"), 503),
            (new TaskCanceledException("test timeout"), 504)
        })
        {
            using var http = new HttpClient(new StubHandler(failure));
            var result = await Proxy(http).GetAttendanceAsync(null, null);
            Assert.Equal(status, result.Status);
            Assert.NotEqual("[]", result.Body);
        }
        using var cancelledHttp = new HttpClient(new StubHandler(new TaskCanceledException()));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Proxy(cancelledHttp).GetAttendanceAsync(null, null, cancelled.Token));
    }

    private static KioskProxy Proxy(HttpClient http)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kiosk:BaseUrl"] = "http://127.0.0.1:8011",
            ["Kiosk:ServiceToken"] = "TEST-ONLY"
        }).Build();
        return new KioskProxy(http, cfg, NullLogger<KioskProxy>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string? _body;
        private readonly Exception? _failure;
        public StubHandler(string body) => _body = body;
        public StubHandler(Exception failure) => _failure = failure;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_failure != null) return Task.FromException<HttpResponseMessage>(_failure);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body!)
            });
        }
    }
}
