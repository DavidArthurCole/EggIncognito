using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices.Fake;

namespace EggIncognito.Tests.Devices;

public class DeviceConnectionFactoryTests {
    private static DeviceCaptureConfig CaptureConfig() => new() {
        IosSshHost = "phone.local",
        IosSshKeyPath = "/keys/phone"
    };

    private static DeviceTarget AndroidTarget => new("a1", Platforms.Android, "SER", "com.auxbrain.egginc");

    private static DeviceTarget IosTarget => new("i1", Platforms.Ios, "UDID", "com.auxbrain.egginc");

    private static DeviceTransportConfig RemoteTransport() => new() {
        Mode = DeviceTransportMode.Remote,
        RemoteBaseUrl = "https://frame.test",
        ApiKey = "k"
    };

    [Fact]
    public void For_NullTransportConfig_AndroidReturnsAdbConnection() {
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig());

        var conn = factory.For(AndroidTarget);

        Assert.IsType<AdbDeviceConnection>(conn);
    }

    [Fact]
    public void For_NullTransportConfig_IosReturnsSshConnection() {
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig());

        var conn = factory.For(IosTarget);

        Assert.IsType<SshDeviceConnection>(conn);
    }

    [Fact]
    public void For_ModeLocalExplicit_AndroidReturnsAdbConnection() {
        var transport = new DeviceTransportConfig { Mode = DeviceTransportMode.Local };
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig(), transport, new StubHttpFactory());

        var conn = factory.For(AndroidTarget);

        Assert.IsType<AdbDeviceConnection>(conn);
    }

    [Fact]
    public void For_ModeLocalExplicit_IosReturnsSshConnection() {
        var transport = new DeviceTransportConfig { Mode = DeviceTransportMode.Local };
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig(), transport, new StubHttpFactory());

        var conn = factory.For(IosTarget);

        Assert.IsType<SshDeviceConnection>(conn);
    }

    [Fact]
    public void For_ModeRemote_AndroidReturnsRemoteConnection() {
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig(), RemoteTransport(), new StubHttpFactory());

        var conn = factory.For(AndroidTarget);

        Assert.IsType<RemoteDeviceConnection>(conn);
    }

    [Fact]
    public void For_ModeRemote_IosReturnsRemoteConnection() {
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig(), RemoteTransport(), new StubHttpFactory());

        var conn = factory.For(IosTarget);

        Assert.IsType<RemoteDeviceConnection>(conn);
    }

    [Fact]
    public void For_ModeRemoteButNoHttpFactory_FallsBackToLocalAdb() {
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig(), RemoteTransport());

        var conn = factory.For(AndroidTarget);

        Assert.IsType<AdbDeviceConnection>(conn);
    }

    [Fact]
    public void Ios_HostAndKeyConfigured_ReturnsSshConnection() {
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig());

        Assert.NotNull(factory.Ios());
    }
}
