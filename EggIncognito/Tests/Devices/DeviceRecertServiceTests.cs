using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Devices.Fake;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class DeviceRecertServiceTests {
    private static UiNode TextNode(string text) => new(null, text, null, "node", null, default, false, true, []);

    private static UiTree Tree(params UiNode[] nodes) {
        var root = new UiNode(null, null, null, "root", null, default, false, true, nodes);
        return new UiTree(root, "");
    }

    private static DeviceJobStore DummyJobStore() {
        var db = new EggIncognitoDbContext(new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none").Options);
        return new DeviceJobStore(db, TimeProvider.System);
    }

    private static DeviceRecertConfig BaseConfig() => new() {
        KsuWebUiPackage = "me.weishu.kernelsu",
        MagiskPackage = "com.topjohnwu.magisk",
        PowerButtonX = 1,
        PowerButtonY = 1,
        RepairTimeoutSeconds = 0,
        MagiskActionWaitSeconds = 0
    };

    [Fact]
    public void Merge_PrimaryFailedFallbackOk_MergedOkTrueLogsAndFieldsConcatenated() {
        var primary = new DeviceFlowResult(
            false, ["p1", "p2"], new Dictionary<string, string> { ["a"] = "1" }, [], "wait text 'x'");
        var fallback = new DeviceFlowResult(true, ["f1"], new Dictionary<string, string> { ["b"] = "2" }, [], null);

        var merged = DeviceRecertService.Merge(primary, fallback, null);

        Assert.True(merged.Ok);
        Assert.Null(merged.FailedStep);
        Assert.Equal(["p1", "p2", "f1"], merged.Log);
        Assert.Equal("1", merged.Fields["a"]);
        Assert.Equal("2", merged.Fields["b"]);
    }

    [Fact]
    public void Merge_BothFailed_FailedStepIsFromTheFallbackPhase() {
        var primary = new DeviceFlowResult(false, ["p1"], new Dictionary<string, string>(), [], "primary-step");
        var fallback = new DeviceFlowResult(false, ["f1"], new Dictionary<string, string>(), [], "fallback-step");

        var merged = DeviceRecertService.Merge(primary, fallback, null);

        Assert.False(merged.Ok);
        Assert.Equal("fallback-step", merged.FailedStep);
    }

    [Fact]
    public void Merge_NoFallbackRan_UsesThePrimaryFailedStep() {
        var primary = new DeviceFlowResult(false, ["p1"], new Dictionary<string, string>(), [], "primary-step");

        var merged = DeviceRecertService.Merge(primary, null, null);

        Assert.False(merged.Ok);
        Assert.Equal("primary-step", merged.FailedStep);
    }

    [Fact]
    public void Merge_VerifyAppendsLogAndFieldsButNeverFlipsOkOrFailedStep() {
        var primary = new DeviceFlowResult(true, ["p1"], new Dictionary<string, string>(), [], null);
        var verify = new DeviceFlowResult(
            true, ["v1 not found"], new Dictionary<string, string> { ["cert"] = "unknown" }, [], null);

        var merged = DeviceRecertService.Merge(primary, null, verify);

        Assert.True(merged.Ok);
        Assert.Null(merged.FailedStep);
        Assert.Equal(["p1", "v1 not found"], merged.Log);
        Assert.Equal("unknown", merged.Fields["cert"]);
    }

    [Fact]
    public async Task RunFlowAsync_PrimaryFails_TriggersFallback_MergedOkTrue() {
        var driver = new FakeUiDriver {
            Dumps = call => call switch {
                0 => DeviceResult<UiTree>.Success(Tree(TextNode("Integrity Hub"))),
                1 => DeviceResult<UiTree>.Success(Tree()),
                _ => DeviceResult<UiTree>.Success(Tree(TextNode("repair complete")))
            }
        };
        var service = new DeviceRecertService(
            [driver], new DeviceConfig(), BaseConfig(), DummyJobStore(), new FakeConnections(new RefusingProcessRunner()),
            NullLogger<DeviceRecertService>.Instance);
        var target = new DeviceTarget("d1", Platforms.Android, "serial", "com.auxbrain.egginc");

        var result = await service.RunFlowAsync(target, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Contains("me.weishu.kernelsu", driver.LaunchCalls);
        Assert.Contains("com.topjohnwu.magisk", driver.LaunchCalls);
    }

    [Fact]
    public async Task RunFlowAsync_PrimarySucceeds_NeverLaunchesMagisk() {
        var driver = new FakeUiDriver {
            Dumps = call => call switch {
                0 => DeviceResult<UiTree>.Success(Tree(TextNode("Integrity Hub"))),
                _ => DeviceResult<UiTree>.Success(Tree(TextNode("repair complete")))
            }
        };
        var config = BaseConfig();
        config.RepairTimeoutSeconds = 5;
        var service = new DeviceRecertService(
            [driver], new DeviceConfig(), config, DummyJobStore(), new FakeConnections(new RefusingProcessRunner()),
            NullLogger<DeviceRecertService>.Instance);
        var target = new DeviceTarget("d1", Platforms.Android, "serial", "com.auxbrain.egginc");

        var result = await service.RunFlowAsync(target, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.DoesNotContain("com.topjohnwu.magisk", driver.LaunchCalls);
    }

    [Fact]
    public async Task RecertAsync_UnknownDevice_RefusesWithLookupFailedStep() {
        var service = new DeviceRecertService(
            [], new DeviceConfig(), BaseConfig(), DummyJobStore(), new FakeConnections(new RefusingProcessRunner()),
            NullLogger<DeviceRecertService>.Instance);

        var result = await service.RecertAsync("missing", "manual", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("lookup", result.FailedStep);
    }

    [Fact]
    public async Task RecertAsync_NonAndroidDevice_RefusesAndroidOnly() {
        var deviceConfig = new DeviceConfig { Devices = [new DeviceEntry("i1", Platforms.Ios, "iPhone", "u", "p")] };
        var service = new DeviceRecertService(
            [], deviceConfig, BaseConfig(), DummyJobStore(), new FakeConnections(new RefusingProcessRunner()),
            NullLogger<DeviceRecertService>.Instance);

        var result = await service.RecertAsync("i1", "manual", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains(result.Log, l => l.Contains("android-only", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecertAsync_KsuWebUiPackageEmpty_RefusesWithoutTouchingTheJobStore() {
        var deviceConfig = new DeviceConfig {
            Devices = [new DeviceEntry("a1", Platforms.Android, "Pixel", "s", "com.auxbrain.egginc")]
        };
        var config = BaseConfig();
        config.KsuWebUiPackage = "";
        var service = new DeviceRecertService(
            [], deviceConfig, config, DummyJobStore(), new FakeConnections(new RefusingProcessRunner()),
            NullLogger<DeviceRecertService>.Instance);

        var result = await service.RecertAsync("a1", "manual", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains(result.Log, l => l.Contains("KsuWebUiPackage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecertAsync_DeviceOnlyInStore_ResolvesFromTheStore() {
        var store = new FakeStatusStore(new Device {
            Id = "i1",
            Platform = Platforms.Ios,
            Label = "iPhone",
            Target = "u",
            Package = "p"
        });
        var service = new DeviceRecertService(
            [], new DeviceConfig(), BaseConfig(), DummyJobStore(), new FakeConnections(new RefusingProcessRunner()),
            NullLogger<DeviceRecertService>.Instance, store);

        var result = await service.RecertAsync("i1", "manual", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains(result.Log, l => l.Contains("android-only", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecertAsync_StoreRowWinsOverTheConfigEntry() {
        var deviceConfig = new DeviceConfig {
            Devices = [new DeviceEntry("d1", Platforms.Android, "Pixel", "s", "com.auxbrain.egginc")]
        };
        var store = new FakeStatusStore(new Device {
            Id = "d1",
            Platform = Platforms.Ios,
            Label = "iPhone",
            Target = "u",
            Package = "p"
        });
        var service = new DeviceRecertService(
            [], deviceConfig, BaseConfig(), DummyJobStore(), new FakeConnections(new RefusingProcessRunner()),
            NullLogger<DeviceRecertService>.Instance, store);

        var result = await service.RecertAsync("d1", "manual", CancellationToken.None);

        Assert.Contains(result.Log, l => l.Contains("android-only", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecertAsync_StoreRowDisabled_RefusesInsteadOfFallingBackToConfig() {
        var deviceConfig = new DeviceConfig {
            Devices = [new DeviceEntry("d1", Platforms.Android, "Pixel", "s", "com.auxbrain.egginc")]
        };
        var store = new FakeStatusStore(new Device {
            Id = "d1",
            Platform = Platforms.Android,
            Label = "Pixel",
            Target = "s",
            Package = "com.auxbrain.egginc",
            Enabled = false
        });
        var service = new DeviceRecertService(
            [], deviceConfig, BaseConfig(), DummyJobStore(), new FakeConnections(new RefusingProcessRunner()),
            NullLogger<DeviceRecertService>.Instance, store);

        var result = await service.RecertAsync("d1", "manual", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("lookup", result.FailedStep);
        Assert.Contains(result.Log, l => l.Contains("unknown device", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecertAsync_StoreHasNoRow_FallsBackToConfig() {
        var deviceConfig = new DeviceConfig { Devices = [new DeviceEntry("i1", Platforms.Ios, "iPhone", "u", "p")] };
        var service = new DeviceRecertService(
            [], deviceConfig, BaseConfig(), DummyJobStore(), new FakeConnections(new RefusingProcessRunner()),
            NullLogger<DeviceRecertService>.Instance, new FakeStatusStore(null));

        var result = await service.RecertAsync("i1", "manual", CancellationToken.None);

        Assert.Contains(result.Log, l => l.Contains("android-only", StringComparison.Ordinal));
    }

    private sealed class FakeStatusStore(Device? row) : IDeviceStatusStore {
        public Task UpsertDeviceAsync(string id, string platform, string label, string target, string package,
            string origin = DeviceOrigins.Runtime, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<Device>> EnabledDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult(row is { Enabled: true } ? [row] : new List<Device>());

        public Task<Device?> GetAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(row is not null && row.Id == id ? row : null);

        public Task RemoveAsync(string id, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetCapturePortAsync(string id, int port, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeUiDriver : IDeviceUiDriver {
        public Func<int, DeviceResult<UiTree>> Dumps = _ => DeviceResult<UiTree>.Success(Tree());
        public int DumpCalls;
        public List<string> LaunchCalls { get; } = [];

        public string Platform => Platforms.Android;

        public Task<DeviceResult<UiTree>> DumpAsync(DeviceTarget target, CancellationToken ct) =>
            Task.FromResult(Dumps(DumpCalls++));

        public Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct) =>
            Task.FromResult(DeviceResult<byte[]>.Success([1, 2, 3]));

        public Task<DeviceResult> TapAsync(DeviceTarget target, UiSelector selector, CancellationToken ct) =>
            Task.FromResult(DeviceResult.Success());

        public Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct) =>
            Task.FromResult(DeviceResult.Success());

        public Task<DeviceResult> InputTextAsync(DeviceTarget target, string text, CancellationToken ct) =>
            Task.FromResult(DeviceResult.Success());

        public Task<DeviceResult> KeyAsync(DeviceTarget target, DeviceKey key, CancellationToken ct) =>
            Task.FromResult(DeviceResult.Success());

        public Task<DeviceResult> LaunchAppAsync(DeviceTarget target, string appRef, CancellationToken ct) {
            LaunchCalls.Add(appRef);
            return Task.FromResult(DeviceResult.Success());
        }
    }

    private sealed class FakeConnections(IProcessRunner runner) : IDeviceConnectionFactory {
        public IDeviceConnection? For(DeviceTarget target) => new AdbDeviceConnection(runner, target.Target);
        public SshDeviceConnection? Ios(string? hostFallback = null) => null;
    }
}
