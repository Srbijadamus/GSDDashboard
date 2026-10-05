namespace GSDDashboard.API.Services;

public static class PulseRuntimeConfiguration
{
    public static string Load(ConfigurationManager configuration)
    {
        var path = Environment.GetEnvironmentVariable("PULSE_RUNTIME_CONFIG");
        if (string.IsNullOrWhiteSpace(path))
            path = configuration["Pulse:RuntimeConfigPath"] ?? @"C:\ProgramData\PulseBackend\backend.json";
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Pulse runtime configuration requires an absolute path.");

        // Explicit reading prevents an absent or ignored optional file from
        // leaving a running but unconfigured proxy.
        using var stream = File.OpenRead(path);
        configuration.AddJsonStream(stream);
        if (string.IsNullOrWhiteSpace(configuration["Kiosk:ServiceToken"]) ||
            string.IsNullOrWhiteSpace(configuration["Kiosk:ServiceDeviceId"]))
            throw new InvalidOperationException("Pulse runtime kiosk service configuration is incomplete.");
        if (!Uri.TryCreate(configuration["Kiosk:BaseUrl"], UriKind.Absolute, out var upstream) ||
            !upstream.IsLoopback || upstream.Scheme != "http")
            throw new InvalidOperationException("Pulse runtime kiosk upstream requires HTTP loopback.");
        return path;
    }
}
