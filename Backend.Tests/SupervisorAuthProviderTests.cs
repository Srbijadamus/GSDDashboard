using System.Security.Cryptography;
using System.Text;
using GSDDashboard.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GSDDashboard.Tests;

// Integration test for the ONE new runtime dependency Pulse gained: it no longer
// accepts a bare KID / employee number, it delegates credential verification to
// the ShiftKiosk account store over HTTP.
//
// These tests talk to a RUNNING TEST INSTANCE of the kiosk server
// (default http://127.0.0.1:8011, database ShiftKioskTest). They return early
// when PULSE_ITEST_* is not set, so a normal `dotnet test` run is unaffected and
// no extra NuGet package (Xunit.SkippableFact) is needed.
//
// To run them, start the test instance and export the account created by
// tools/admin_cli.py:
//     set PULSE_ITEST_BASE=http://127.0.0.1:8011
//     set PULSE_ITEST_USER=pulse.itest
//     set PULSE_ITEST_PASS=...
//     set PULSE_ITEST_TOTP=<base32 secret>
public class SupervisorAuthProviderTests
{
    private static string? Env(string k) => Environment.GetEnvironmentVariable(k);

    private static bool Configured =>
        !string.IsNullOrWhiteSpace(Env("PULSE_ITEST_USER")) &&
        !string.IsNullOrWhiteSpace(Env("PULSE_ITEST_PASS")) &&
        !string.IsNullOrWhiteSpace(Env("PULSE_ITEST_TOTP"));

    private static ShiftKioskSupervisorAuthProvider Provider(string? baseUrl = null)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:SupervisorVerifyUrl"] = baseUrl ?? Env("PULSE_ITEST_BASE") ?? "http://127.0.0.1:8011"
        }).Build();
        return new ShiftKioskSupervisorAuthProvider(
            new HttpClient(), cfg,
            NullLogger<ShiftKioskSupervisorAuthProvider>.Instance);
    }

    /// <summary>RFC 6238, HMAC-SHA1, 6 digits, 30 s — the same contract as
    /// security/crypto.py on the kiosk side.</summary>
    private static string Totp(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        long counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(msg);
        int offset = hash[^1] & 0x0F;
        int bin = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16)
                | (hash[offset + 2] << 8) | hash[offset + 3];
        return (bin % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string s)
    {
        const string A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        s = s.TrimEnd('=').ToUpperInvariant();
        var bits = new StringBuilder();
        foreach (var c in s)
        {
            int idx = A.IndexOf(c);
            if (idx < 0) continue;
            bits.Append(Convert.ToString(idx, 2).PadLeft(5, '0'));
        }
        var bytes = new List<byte>();
        for (int i = 0; i + 8 <= bits.Length; i += 8)
            bytes.Add(Convert.ToByte(bits.ToString(i, 8), 2));
        return bytes.ToArray();
    }

    [Fact]
    public async Task ValidCredentials_ReturnIdentityWithRole()
    {
        if (!Configured) return;   // test instance not running - nothing to verify
        var id = await Provider().VerifyAsync(Env("PULSE_ITEST_USER")!, Env("PULSE_ITEST_PASS")!,
                                              Totp(Env("PULSE_ITEST_TOTP")!), null);
        Assert.NotNull(id);
        Assert.Equal(Env("PULSE_ITEST_USER"), id!.Username);
        Assert.Equal("RTM", id.Role);
        Assert.True(id.MustChangePassword, "a freshly created account must force a password change");
    }

    [Fact]
    public async Task WrongPassword_ReturnsNull_NotAnException()
    {
        if (!Configured) return;   // test instance not running - nothing to verify
        var id = await Provider().VerifyAsync(Env("PULSE_ITEST_USER")!, "definitely-wrong",
                                              Totp(Env("PULSE_ITEST_TOTP")!), null);
        Assert.Null(id);
    }

    [Fact]
    public async Task MissingSecondFactor_IsRefused()
    {
        if (!Configured) return;   // test instance not running - nothing to verify
        var id = await Provider().VerifyAsync(Env("PULSE_ITEST_USER")!, Env("PULSE_ITEST_PASS")!,
                                              null, null);
        Assert.Null(id);
    }

    [Fact]
    public async Task ProviderDown_RaisesUnavailable_SoPulseCanTellOutageFromBadPassword()
    {
        // Port 9 (discard) is never listening. The distinction matters: an outage
        // must not be reported to the user as "wrong password", and Pulse answers
        // 503 instead of 401 in that case (AuthModule.cs login handler).
        await Assert.ThrowsAsync<SupervisorAuthUnavailableException>(() =>
            Provider("http://127.0.0.1:9").VerifyAsync("x", "y", "123456", null));
    }
}
