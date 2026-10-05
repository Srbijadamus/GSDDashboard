using GSDDashboard.API.Services;
using Microsoft.Extensions.Configuration;

namespace GSDDashboard.Tests;

public class PulseRuntimeConfigurationTests
{
    [Fact]
    public void ExplicitRuntimeFileOverridesEmptyDefaults_AndRejectsMissingOrIncompleteFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pulse-config-test-{Guid.NewGuid():N}.json");
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Pulse:RuntimeConfigPath"] = path,
            ["Kiosk:ServiceToken"] = "",
        });
        Assert.Throws<FileNotFoundException>(() => PulseRuntimeConfiguration.Load(config));
        try
        {
            File.WriteAllText(path, """
                {"Kiosk":{"BaseUrl":"http://localhost:8000","ServiceToken":"test-only-placeholder","ServiceDeviceId":"test-device"}}
                """);
            Assert.Equal(path, PulseRuntimeConfiguration.Load(config));
            Assert.Equal("test-only-placeholder", config["Kiosk:ServiceToken"]);
            File.WriteAllText(path, """{"Kiosk":{"ServiceToken":""}}""");
            Assert.Throws<InvalidOperationException>(() => PulseRuntimeConfiguration.Load(config));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
