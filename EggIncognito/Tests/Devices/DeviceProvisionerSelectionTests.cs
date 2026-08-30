using EggIncognito.Core.Services.Devices;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class DeviceProvisionerSelectionTests : IDisposable {
    private readonly DockerEngineClient _docker = new("/var/run/docker.sock");

    private static DeviceTransportConfig Transport() => new() {
        Mode = DeviceTransportMode.Remote,
        RemoteBaseUrl = "https://frame.test",
        ApiKey = "k"
    };

    private DeviceProvisioners Registry() => new([
        new RedroidProvisioner(_docker, new VirtualDeviceConfig(), TimeProvider.System,
            NullLogger<RedroidProvisioner>.Instance),
        new RemoteDeviceProvisioner(new StubHttpFactory(), Transport())
    ]);

    [Fact]
    public void For_Redroid_ReturnsRedroidProvisioner() =>
        Assert.IsType<RedroidProvisioner>(Registry().For("redroid"));

    [Fact]
    public void For_Remote_ReturnsRemoteProvisioner() =>
        Assert.IsType<RemoteDeviceProvisioner>(Registry().For(RemoteDeviceProvisioner.KindName));

    [Fact]
    public void For_RemoteDifferentCasing_ReturnsRemoteProvisioner() =>
        Assert.IsType<RemoteDeviceProvisioner>(Registry().For("Remote"));

    [Fact]
    public void For_UnknownKind_FallsBackToNullProvisioner() =>
        Assert.IsType<NullDeviceProvisioner>(Registry().For("emulator"));

    [Fact]
    public void Kinds_ContainBothRegisteredProvisioners() {
        var kinds = Registry().Kinds;

        Assert.Contains("redroid", kinds);
        Assert.Contains(RemoteDeviceProvisioner.KindName, kinds);
    }

    [Fact]
    public void DefaultConfigKind_IsRedroidSoTheLocalPathIsUnchanged() {
        var config = new VirtualDeviceConfig();

        Assert.Equal("redroid", config.Kind);
        Assert.False(RemoteDeviceProvisioner.IsRemoteKind(config.Kind));
    }

    public void Dispose() {
        _docker.Dispose();
        GC.SuppressFinalize(this);
    }
}
