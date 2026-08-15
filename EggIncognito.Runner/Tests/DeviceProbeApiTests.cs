using EggIncognito.Core.Services.Devices;
using EggIncognito.Runner.Data;
using EggIncognito.Runner.Trigger;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class DeviceProbeApiTests {
    private static DeviceProbeApi Api() {
        var db = RunnerDb.FromEnv(k => k == "ConnectionStrings__Postgres" ? "Host=localhost;Database=x" : "")!;
        return new DeviceProbeApi("secret", db, new DevicePlatforms([]), TimeProvider.System,
            NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task ProbeOne_BadBearer_Is401() {
        var r = await Api().ProbeOneAsync("Bearer wrong", "pixel", "admin:test");
        Assert.Equal(401, r.Status);
    }

    [Fact]
    public async Task ProbeAll_BadBearer_Is401() {
        var r = await Api().ProbeAllAsync("Bearer wrong", "admin-all:test");
        Assert.Equal(401, r.Status);
    }

    [Fact]
    public async Task ProbeOne_MissingBearer_Is401() {
        var r = await Api().ProbeOneAsync(null, "pixel", "admin:test");
        Assert.Equal(401, r.Status);
    }
}
