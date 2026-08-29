using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;
using EggIncognito.Runner.Adb;
using EggIncognito.Runner.Data;
using EggIncognito.Runner.Devices;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.Harvest;
using EggIncognito.Runner.Posting;
using EggIncognito.Runner.Runners;
using EggIncognito.Runner.State;
using EggIncognito.Runner.Trigger;

namespace EggIncognito.Runner;

public static class Program {
    public static async Task<int> Main(string[] args) {
        static string Env(string k, string fb = "") =>
            Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : fb;

        var package = Env("PACKAGE", "com.auxbrain.egginc");
        var apkStash = Env("APK_STASH_DIR", "apks");
        var devicesDir = Env("DEVICES_DIR");
        var interval = int.TryParse(Env("POLL_INTERVAL"), out var s) ? s : 300;
        var eventUrl = Env("SYNC_EVENT_URL");
        var eventSecret = Env("SYNC_EVENT_SECRET");
        var triggerSecret = Env("RUNNER_TRIGGER_SECRET");
        var triggerUrls = Env("RUNNER_TRIGGER_URLS", "http://127.0.0.1:5055");
        var iosBinary = Env("IOS_BINARY_PATH", Path.Combine(apkStash, "ios-binary"));
        int? prevCv = int.TryParse(Env("PREV_CLIENT_VERSION"), out var pcv) ? pcv : null;

        Directory.CreateDirectory(apkStash);

        using var shutdown = new CancellationTokenSource();
        using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; shutdown.Cancel(); });
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        var ct = shutdown.Token;

        var http = new HttpClient();
        var poster = new EventPoster(http, eventUrl, eventSecret);
        var clientVersion = new LibegincClientVersionReader();

        var deps = new RunnerDeps(
            new CSharpProtoExtractor(), clientVersion, apkStash, iosBinary, prevCv, package,
            evt => poster.PostAsync(evt).GetAwaiter().GetResult());
        var runnerDb = RunnerDb.FromEnv(k => Env(k));

        var devices = RunnerDeviceSource.Read(devicesDir);
        var set = RunnerSet.Build(devices, deps, () => LegacyRunner(Env, deps, package, iosBinary));

        if (set.Runners.Count == 0) {
            Console.Error.WriteLine("no runnable devices configured (set DEVICES_DIR or PLATFORM+target)");
            return 1;
        }

        var once = args.Contains("--once");
        var force = args.Contains("--force");
        var serve = args.Contains("--serve") || triggerSecret.Length > 0;

        if (once) {
            foreach (var r in set.Runners) {
                var outcome = r.RunOnce(force);
                Console.WriteLine($"{r.Platform} once force={force}: {outcome.Detail} build={outcome.Build}");
            }
            return 0;
        }

        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var sweepLogger = loggerFactory.CreateLogger("RunnerProbeSweep");
        var procRunner = new ProcessRunner();

        var appConfig = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var captureConfig = DeviceCaptureConfig.Bind(appConfig);
        var connections = new DeviceConnectionFactory(procRunner, captureConfig);
        var devicePlatforms = new DevicePlatforms([
            new AndroidPlatform(procRunner, appConfig, [], [], [], [], loggerFactory.CreateLogger<AndroidPlatform>()),
            new IosPlatform(connections, captureConfig, procRunner, [], [], [], [],
                loggerFactory.CreateLogger<IosPlatform>())
        ]);

        HarvestScheduler? harvester = null;
        if (runnerDb is not null) {
            harvester = new HarvestScheduler(runnerDb, devicePlatforms, loggerFactory);
            using var resetCtx = runnerDb.NewContext();
            int stuck = await new DeviceStateStore(resetCtx).ResetRunningAsync(ct);
            if (stuck > 0) Console.WriteLine($"cleared {stuck} interrupted harvest(s)");
        }

        DeviceResyncHandler? handler = null;
        WebApplication? trigger = null;
        if (serve && triggerSecret.Length > 0) {
            handler = new DeviceResyncHandler(triggerSecret, set.ById);
            var extractHandler = new ApkPureExtractHandler(
                triggerSecret, new ApkPureDownloader(http),
                new CSharpProtoExtractor(), clientVersion,
                new ClientVersionState(Path.Combine(apkStash, "clientversion-apkpure.txt"), prevCv),
                evt => poster.PostAsync(evt));
            if (runnerDb is not null && harvester is not null) {
                var probeApi = new DeviceProbeApi(triggerSecret, runnerDb, devicePlatforms, TimeProvider.System, loggerFactory);
                var harvestApi = new HarvestApi(triggerSecret, runnerDb, harvester);
                trigger = TriggerListener.Build(triggerUrls, handler, extractHandler, probeApi, harvestApi);
            } else {
                trigger = TriggerListener.Build(triggerUrls, handler, extractHandler);
            }
            await trigger.StartAsync(ct);
            Console.WriteLine($"resync trigger listening on {triggerUrls}");
        }

        Console.WriteLine($"runner watching {set.Runners.Count} device(s) every {interval}s");
        while (!ct.IsCancellationRequested) {
            foreach (var r in set.Runners) {
                if (ct.IsCancellationRequested) break;
                try {
                    var tick = Task.Run(() => r.RunOnce(force: false));
                    var done = await Task.WhenAny(tick, Task.Delay(Timeout.Infinite, ct));
                    if (done != tick) break;
                    var outcome = await tick;
                    if (outcome.Emitted) Console.WriteLine($"{r.Platform} emitted build {outcome.Build}");
                } catch (OperationCanceledException) { break; } catch (Exception ex) {
                    Console.Error.WriteLine($"{r.Platform} tick error: {ex.Message}");
                }
            }
            if (runnerDb is not null) {
                try { await RunnerProbeSweep.RunAsync(runnerDb, devicePlatforms, TimeProvider.System, sweepLogger, ct); } catch (OperationCanceledException) { throw; } catch (Exception ex) {
                    Console.Error.WriteLine($"probe sweep error: {ex.Message}");
                }
            }
            if (harvester is not null) {
                try { await harvester.PokeAllAsync(ct); } catch (OperationCanceledException) { throw; } catch (Exception ex) {
                    Console.Error.WriteLine($"harvest poke error: {ex.Message}");
                }
            }
            try { await Task.Delay(interval * 1000, ct); } catch (OperationCanceledException) { break; }
        }
        Console.WriteLine("runner shutting down");
        if (trigger is not null) {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await trigger.StopAsync(stopCts.Token); } catch (OperationCanceledException ex) {
                Console.Error.WriteLine($"trigger listener did not stop in time: {ex.Message}");
            }
        }
        return 0;
    }

    private static IDeviceRunner? LegacyRunner(
        Func<string, string, string> env, RunnerDeps deps, string package, string iosBinary) {
        var platform = env("PLATFORM", "");
        if (platform.Length == 0) return null;
        var target = env("ADB_TARGET", "127.0.0.1:5555");
        var stateFile = env("STATE_FILE", $"state-{platform}.json");
        return platform switch {
            "android" => new AndroidRunner(
                new AdbClient(target), deps.Proto, new VersionState(stateFile),
                deps.ClientVersion,
                new ClientVersionState(Path.Combine(deps.ApkStashDir, $"clientversion-{platform}.txt"), deps.PrevClientVersion),
                package, deps.ApkStashDir, deps.OnNewVersion),
            "ios" => new IosRunner(iosBinary, new VersionState(stateFile), package, deps.OnNewVersion),
            _ => null,
        };
    }
}
