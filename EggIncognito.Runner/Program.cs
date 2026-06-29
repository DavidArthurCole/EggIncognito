using EggIncognito.Core.Models;
using EggIncognito.Runner.Adb;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.Posting;
using EggIncognito.Runner.Runners;
using EggIncognito.Runner.State;
using EggIncognito.Runner.Trigger;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Runner;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string Env(string k, string fb = "") =>
            Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : fb;

        var platform = Env("PLATFORM", "android");
        var package = Env("PACKAGE", "com.auxbrain.egginc");
        var target = Env("ADB_TARGET", "127.0.0.1:5555");
        var stateFile = Env("STATE_FILE", $"state-{platform}.json");
        var apkStash = Env("APK_STASH_DIR", "apks");
        var interval = int.TryParse(Env("POLL_INTERVAL"), out var s) ? s : 300;
        var eventUrl = Env("SYNC_EVENT_URL");
        var eventSecret = Env("SYNC_EVENT_SECRET");
        var triggerSecret = Env("RUNNER_TRIGGER_SECRET");
        var triggerUrls = Env("RUNNER_TRIGGER_URLS", "http://127.0.0.1:5055");
        var iosBinary = Env("IOS_BINARY_PATH", Path.Combine(apkStash, "ios-binary"));

        Directory.CreateDirectory(apkStash);

        // Graceful shutdown: SIGTERM (systemctl stop/restart) and Ctrl+C cancel the poll loop so the
        // process exits promptly instead of waiting for systemd's 90s kill timeout.
        using var shutdown = new CancellationTokenSource();
        using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; shutdown.Cancel(); });
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        var ct = shutdown.Token;

        var http = new HttpClient();
        var poster = new EventPoster(http, eventUrl, eventSecret);
        var clientVersion = new LibegincClientVersionReader();
        int? prevCv = int.TryParse(Env("PREV_CLIENT_VERSION"), out var pcv) ? pcv : null;
        var cvState = new ClientVersionState(Path.Combine(apkStash, $"clientversion-{platform}.txt"), prevCv);

        IDeviceRunner runner = platform switch
        {
            "android" => new AndroidRunner(
                new AdbClient(target), new CSharpProtoExtractor(),
                new VersionState(stateFile), clientVersion, cvState, package, apkStash,
                evt => poster.PostAsync(evt).GetAwaiter().GetResult()),
            "ios" => new IosRunner(
                iosBinary, new VersionState(stateFile), package,
                evt => poster.PostAsync(evt).GetAwaiter().GetResult()),
            _ => throw new InvalidOperationException($"unknown PLATFORM {platform}"),
        };

        var once = args.Contains("--once");
        var force = args.Contains("--force");
        var serve = args.Contains("--serve") || triggerSecret.Length > 0;

        if (once)
        {
            var outcome = runner.RunOnce(force);
            Console.WriteLine($"{platform} once force={force}: {outcome.Detail} build={outcome.Build}");
            return 0;
        }

        ResyncHandler? handler = null;
        WebApplication? trigger = null;
        if (serve && triggerSecret.Length > 0)
        {
            handler = new ResyncHandler(triggerSecret, f => runner.RunOnce(f));
            var extractHandler = new ApkPureExtractHandler(
                triggerSecret, new ApkPureDownloader(http),
                new CSharpProtoExtractor(), clientVersion, cvState,
                evt => poster.PostAsync(evt));
            trigger = TriggerListener.Build(triggerUrls, handler, extractHandler);
            // Start without handing SIGTERM to the web host's lifetime. StartAsync (not RunAsync) means we
            // own shutdown: the host won't install its own ConsoleLifetime signal handler to race ours, and
            // we explicitly stop it below. RunAsync left the host un-awaited and competing for SIGTERM,
            // which is why shutdown stalled to systemd's kill timeout.
            await trigger.StartAsync(ct);
            Console.WriteLine($"resync trigger listening on {triggerUrls}");
        }

        Console.WriteLine($"runner watching {package} on {target} ({platform}) every {interval}s");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // RunOnce is synchronous and ct-blind (adb poll / APK download / proto extract). Run it on a
                // worker and race it against cancellation so SIGTERM returns control to the loop immediately
                // instead of waiting for an in-flight tick to finish.
                var tick = Task.Run(() => runner.RunOnce(force: false));
                var done = await Task.WhenAny(tick, Task.Delay(Timeout.Infinite, ct));
                if (done != tick) break; // cancelled mid-tick; abandon the worker and exit
                var outcome = await tick;
                if (outcome.Emitted) Console.WriteLine($"emitted build {outcome.Build}");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"tick error: {ex.Message}");
            }
            try { await Task.Delay(interval * 1000, ct); }
            catch (OperationCanceledException) { break; }
        }
        Console.WriteLine("runner shutting down");
        if (trigger is not null)
        {
            // Stop the listener with a bounded timeout so a hung in-flight request can't outlive the unit's
            // TimeoutStopSec. The cgroup SIGKILL backstops anything still stuck after this.
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await trigger.StopAsync(stopCts.Token); }
            catch (OperationCanceledException) { /* forced kill backstops a stuck host */ }
        }
        return 0;
    }
}
