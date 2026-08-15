using EggIncognito.Services;
using EggIncognito.Services.Devices.Fake;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests.Devices;

public class FakeDeviceGateTests {
    private static IConfiguration Cfg(string? enabled) {
        Dictionary<string, string?> values = [];
        if (enabled is not null) values[FakeDeviceGate.EnabledKey] = enabled;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void IsOn_OnlyForStagingLocalAndAnExplicitTrue() =>
        Assert.True(FakeDeviceGate.IsOn("Staging", AppMode.Local, Cfg("true")));

    [Fact]
    public void IsOn_False_WhenKeyAbsent() =>
        Assert.False(FakeDeviceGate.IsOn("Staging", AppMode.Local, Cfg(null)));

    [Fact]
    public void IsOn_False_WhenKeyIsFalse() =>
        Assert.False(FakeDeviceGate.IsOn("Staging", AppMode.Local, Cfg("false")));

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("")]
    [InlineData("staging-2")]
    [InlineData("SubProd")]
    public void IsOn_False_ForAnyEnvironmentThatIsNotStaging(string environment) =>
        Assert.False(FakeDeviceGate.IsOn(environment, AppMode.Local, Cfg("true")));

    [Fact]
    public void IsOn_StagingIsCaseInsensitive() =>
        Assert.True(FakeDeviceGate.IsOn("staging", AppMode.Local, Cfg("true")));

    [Fact]
    public void IsOn_False_WhenModeIsHosted() =>
        Assert.False(FakeDeviceGate.IsOn("Staging", AppMode.Hosted, Cfg("true")));

    [Fact]
    public void Guard_Throws_ForProductionWithTheKeySet() {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FakeDeviceGate.Guard("Production", AppMode.Local, Cfg("true")));
        Assert.Contains(FakeDeviceGate.EnabledKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_Throws_ForHostedWithTheKeySet() {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FakeDeviceGate.Guard("Staging", AppMode.Hosted, Cfg("true")));
        Assert.Contains(FakeDeviceGate.EnabledKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_Silent_WhenTheKeyIsAbsent() {
        FakeDeviceGate.Guard("Production", AppMode.Hosted, Cfg(null));
        FakeDeviceGate.Guard("Production", AppMode.Hosted, Cfg("false"));
    }

    [Fact]
    public void Guard_Silent_ForStagingLocal() =>
        FakeDeviceGate.Guard("Staging", AppMode.Local, Cfg("true"));

    [Fact]
    public void Guard_Silent_ForDevelopmentLocal() =>
        FakeDeviceGate.Guard("Development", AppMode.Local, Cfg("true"));

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void ShippedAppSettings_NeverDeclareFakeDevices(string file) {
        string path = Path.Combine(RepoRoot(), "EggIncognito", file);
        Assert.True(File.Exists(path), path);
        string json = File.ReadAllText(path);
        Assert.DoesNotContain("Devices:Fake", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Fake\"", json, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
