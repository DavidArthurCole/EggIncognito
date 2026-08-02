using System.Net;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class StoreUpdateTests {
    private const string UiWithUpdate =
        "<hierarchy><node text=\"Update\" bounds=\"[718,551][851,608]\"/>" +
        "<node text=\"Uninstall\" bounds=\"[200,551][333,608]\"/></hierarchy>";

    private const string UiNoUpdate =
        "<hierarchy><node text=\"Open\" bounds=\"[718,551][851,608]\"/>" +
        "<node text=\"Uninstall\" bounds=\"[200,551][333,608]\"/></hierarchy>";

    private const string UiMajorUpdate =
        "<hierarchy><node text=\"Play\" bounds=\"[718,551][851,608]\"/>" +
        "<node text=\"Uninstall\" bounds=\"[200,551][333,608]\"/>" +
        "<node text=\"Update available\" bounds=\"[113,1683][376,1728]\"/></hierarchy>";

    private static DeviceTarget AndroidTarget => new("a", "android", "SER", "com.auxbrain.egginc");

    private static DeviceTarget IosTarget => new("i", "ios", "UDID", "com.auxbrain.egginc");

    private static KnownVersionRecorder Recorder() =>
        new(new NullScopeFactory(), NullLogger<KnownVersionRecorder>.Instance);

    private static StoreUpdateOrchestrator Orchestrator(IStoreUpdateDriver driver, int attempts = 3) =>
        new(driver, new StoreUpdateOrchestrator.Options(0, attempts), Recorder(), NullLogger.Instance);

    private static StoreUpdateOrchestrator AndroidOrchestrator(FakeRunner runner, int attempts = 3) =>
        Orchestrator(new AndroidStoreUpdateDriver(runner,
                new AndroidStoreUpdateDriver.Options("am start {package}", 0, 0),
                NullLogger<AndroidStoreUpdateDriver>.Instance),
            attempts);

    private static IosStoreCatalog Catalog(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new StubHttpFactory(new StubHandler(respond)), NullLogger<IosStoreCatalog>.Instance);

    private static IosStoreUpdateDriver IosDriver(IosStoreCatalog catalog) =>
        new(new FakeRunner(_ => new ProcessResult(0, "", "")),
            new IosStoreUpdateDriver.Options(null, "22", null, "/var/mobile/trigger", "12345", null),
            catalog, Recorder(), NullLogger<IosStoreUpdateDriver>.Instance);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public async Task Orchestrator_NoInstalledRead_Unreachable() {
        var driver = new FakeDriver { InstalledReads = _ => null };
        var rounds = new List<string>();

        var result = await Orchestrator(driver).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("unreachable", result.Action);
        Assert.False(result.Reachable);
        Assert.False(driver.CleanupCalled);
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Orchestrator_ProbeUpToDate_ReturnsFast() {
        var driver = new FakeDriver { Probe = new StoreProbeOutcome(StoreAvailability.UpToDate, "1.0", null) };
        var rounds = new List<string>();

        var result = await Orchestrator(driver).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("up_to_date", result.Action);
        Assert.False(result.Installed);
        Assert.False(driver.TriggerCalled);
        Assert.True(driver.CleanupCalled);
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Orchestrator_ProbeManualNeeded_NoTrigger() {
        var driver = new FakeDriver {
            Probe = new StoreProbeOutcome(StoreAvailability.ManualNeeded, null, "needs manual update")
        };

        var result = await Orchestrator(driver).CheckAndUpdateAsync(AndroidTarget, default);

        Assert.Equal("manual_needed", result.Action);
        Assert.True(result.UpdateFound);
        Assert.False(result.Installed);
        Assert.Equal("needs manual update", result.Note);
        Assert.False(driver.TriggerCalled);
        Assert.True(driver.CleanupCalled);
    }

    [Fact]
    public async Task Orchestrator_TriggerFails_Error() {
        var driver = new FakeDriver {
            Probe = new StoreProbeOutcome(StoreAvailability.UpdateOffered, "2.0", null),
            Trigger = new TriggerOutcome(false, "tap failed")
        };

        var result = await Orchestrator(driver).CheckAndUpdateAsync(AndroidTarget, default);

        Assert.Equal("error", result.Action);
        Assert.Equal("tap failed", result.Note);
        Assert.True(result.UpdateFound);
        Assert.False(result.Installed);
        Assert.True(driver.TriggerCalled);
        Assert.True(driver.CleanupCalled);
    }

    [Fact]
    public async Task Orchestrator_UnknownProbe_VersionClimb_Updated() {
        var driver = new FakeDriver { InstalledReads = i => i >= 2 ? "1.1" : "1.0" };
        var rounds = new List<string>();

        var result = await Orchestrator(driver, 10).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("updated", result.Action);
        Assert.True(result.Installed);
        Assert.Equal("1.0", result.InstalledBefore);
        Assert.Equal("1.1", result.InstalledAfter);
        Assert.True(driver.TriggerCalled);
        Assert.True(driver.CleanupCalled);
        Assert.Contains(rounds, m => m.Contains("1.1") && m.Contains("1.0"));
    }

    [Fact]
    public async Task Orchestrator_NullProgress_NoThrow() {
        var driver = new FakeDriver { Probe = new StoreProbeOutcome(StoreAvailability.UpToDate, "1.0", null) };

        var result = await Orchestrator(driver).CheckAndUpdateAsync(AndroidTarget, default);

        Assert.Equal("up_to_date", result.Action);
    }

    [Fact]
    public async Task Android_UpToDate_WhenNoUpdateButton() {
        var runner = new FakeRunner(args => {
            return args.Contains("dumpsys")
                ? new ProcessResult(0, "versionName=1.0\n", "")
                : args.Any(a => a.Contains("cat"))
                    ? new ProcessResult(0, UiNoUpdate, "")
                    : new ProcessResult(0, "", "");
        });

        var rounds = new List<string>();
        var result = await AndroidOrchestrator(runner).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("up_to_date", result.Action);
        Assert.False(result.Installed);
        Assert.NotEmpty(rounds);
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Android_MajorUpdateAdvertised_ManualNeeded() {
        var runner = new FakeRunner(args => {
            return args.Contains("dumpsys")
                ? new ProcessResult(0, "versionName=1.0\n", "")
                : args.Any(a => a.Contains("cat"))
                    ? new ProcessResult(0, UiMajorUpdate, "")
                    : new ProcessResult(0, "", "");
        });

        var result = await AndroidOrchestrator(runner).CheckAndUpdateAsync(AndroidTarget, default);

        Assert.Equal("manual_needed", result.Action);
        Assert.True(result.UpdateFound);
        Assert.False(result.Installed);
    }

    [Fact]
    public async Task Android_PageNeverLoads_Error() {
        var runner = new FakeRunner(args => {
            return args.Contains("dumpsys")
                ? new ProcessResult(0, "versionName=1.0\n", "")
                : new ProcessResult(0, "", "");
        });

        var result = await AndroidOrchestrator(runner, 2).CheckAndUpdateAsync(AndroidTarget, default);

        Assert.Equal("error", result.Action);
    }

    [Fact]
    public async Task Android_ProgressAnnouncesClimb_ThenUpdated() {
        int dumpsys = 0;
        var runner = new FakeRunner(args => {
            if (args.Contains("dumpsys")) {
                string v = dumpsys++ >= 2 ? "1.1" : "1.0";
                return new ProcessResult(0, $"versionName={v}\n", "");
            }

            return args.Any(a => a.Contains("cat"))
                ? new ProcessResult(0, UiWithUpdate, "")
                : new ProcessResult(0, "", "");
        });

        var rounds = new List<string>();
        var result = await AndroidOrchestrator(runner, 10).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("updated", result.Action);
        Assert.True(result.Installed);
        Assert.Equal("1.0", result.InstalledBefore);
        Assert.Equal("1.1", result.InstalledAfter);
        Assert.Contains(rounds, m => m.Contains("1.1") && m.Contains("1.0"));
    }

    [Fact]
    public async Task Android_EmptyDumpsys_Unreachable() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var rounds = new List<string>();

        var result = await AndroidOrchestrator(runner).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("unreachable", result.Action);
        Assert.False(result.Reachable);
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindUpdateButtonCenter_ParsesBoundsCenter() {
        var c = AndroidStoreUpdateDriver.FindUpdateButtonCenter(UiWithUpdate);
        Assert.NotNull(c);
        Assert.Equal((784, 579), c.Value);
    }

    [Fact]
    public void FindUpdateButtonCenter_NoUpdate_ReturnsNull() =>
        Assert.Null(AndroidStoreUpdateDriver.FindUpdateButtonCenter(UiNoUpdate));

    [Fact]
    public async Task Catalog_ValidLookup_ReturnsVersion() {
        var catalog = Catalog(_ => Json("{\"resultCount\":1,\"results\":[{\"version\":\"1.37\"}]}"));
        Assert.Equal("1.37", await catalog.LatestVersionAsync("12345", null, default));
    }

    [Fact]
    public async Task Catalog_NoResults_ReturnsNull() {
        var catalog = Catalog(_ => Json("{\"resultCount\":0,\"results\":[]}"));
        Assert.Null(await catalog.LatestVersionAsync("12345", null, default));
    }

    [Fact]
    public async Task Catalog_HttpError_ReturnsNull() {
        var catalog = Catalog(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Null(await catalog.LatestVersionAsync("12345", null, default));
    }

    [Fact]
    public async Task Catalog_MalformedJson_ReturnsNull() {
        var catalog = Catalog(_ => Json("not json at all"));
        Assert.Null(await catalog.LatestVersionAsync("12345", null, default));
    }

    [Fact]
    public async Task IosProbe_StoreMatchesInstalled_UpToDate() {
        var driver = IosDriver(Catalog(_ => Json("{\"resultCount\":1,\"results\":[{\"version\":\"1.36\"}]}")));

        var probe = await driver.ProbeStoreAsync(IosTarget, "1.36", null, default);

        Assert.Equal(StoreAvailability.UpToDate, probe.Availability);
        Assert.Equal("1.36", probe.StoreVersion);
    }

    [Fact]
    public async Task IosProbe_StoreAhead_UpdateOffered() {
        var driver = IosDriver(Catalog(_ => Json("{\"resultCount\":1,\"results\":[{\"version\":\"1.37\"}]}")));

        var probe = await driver.ProbeStoreAsync(IosTarget, "1.36", null, default);

        Assert.Equal(StoreAvailability.UpdateOffered, probe.Availability);
        Assert.Equal("1.37", probe.StoreVersion);
    }

    [Fact]
    public async Task IosProbe_LookupFails_Unknown() {
        var driver = IosDriver(Catalog(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var probe = await driver.ProbeStoreAsync(IosTarget, "1.36", null, default);

        Assert.Equal(StoreAvailability.Unknown, probe.Availability);
        Assert.Null(probe.StoreVersion);
    }

    private sealed class FakeDriver : IStoreUpdateDriver {
        public Func<int, string?> InstalledReads = _ => "1.0";
        public StoreProbeOutcome Probe = new(StoreAvailability.Unknown, null, null);
        public TriggerOutcome Trigger = new(true, null);
        public bool TriggerCalled;
        public bool CleanupCalled;
        private int _reads;

        public string Platform => "test";
        public string StoreName => "Store";

        public Task<string?> ReadInstalledAsync(DeviceTarget target, CancellationToken ct) =>
            Task.FromResult(InstalledReads(_reads++));

        public Task PrepareAsync(DeviceTarget target, CancellationToken ct) => Task.CompletedTask;

        public Task<StoreProbeOutcome> ProbeStoreAsync(
            DeviceTarget target, string installed, Action<string>? progress, CancellationToken ct) =>
            Task.FromResult(Probe);

        public Task<TriggerOutcome> TriggerInstallAsync(
            DeviceTarget target, Action<string>? progress, CancellationToken ct) {
            TriggerCalled = true;
            return Task.FromResult(Trigger);
        }

        public Task CleanupAsync(DeviceTarget target, CancellationToken ct) {
            CleanupCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRunner(Func<string[], ProcessResult> fn) : IProcessRunner {
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) =>
            Task.FromResult(fn(args));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubHttpFactory(HttpMessageHandler handler) : IHttpClientFactory {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class NullScopeFactory : IServiceScopeFactory {
        public IServiceScope CreateScope() => new NullScope();

        private sealed class NullScope : IServiceScope {
            public IServiceProvider ServiceProvider { get; } = new NullProvider();

            public void Dispose() {
            }
        }

        private sealed class NullProvider : IServiceProvider {
            public object? GetService(Type serviceType) => null;
        }
    }
}
