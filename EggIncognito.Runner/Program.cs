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
        var extractorRepo = Env("EXTRACTOR_REPO", "../tools/proto-extract");
        var extractorPython = Env("EXTRACTOR_PYTHON", Path.Combine(extractorRepo, ".venv", "bin", "python3"));
        var triggerSecret = Env("RUNNER_TRIGGER_SECRET");
        var triggerUrls = Env("RUNNER_TRIGGER_URLS", "http://127.0.0.1:5055");

        Directory.CreateDirectory(apkStash);

        var http = new HttpClient();
        var poster = new EventPoster(http, eventUrl, eventSecret);
        var clientVersion = new NullClientVersionReader();

        IDeviceRunner runner = platform switch
        {
            "android" => new AndroidRunner(
                new AdbClient(target), new PbtkProtoExtractor(extractorRepo, extractorPython),
                new VersionState(stateFile), clientVersion, package, apkStash,
                evt => poster.PostAsync(evt).GetAwaiter().GetResult()),
            "ios" => new IosRunner(),
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
                new PbtkProtoExtractor(extractorRepo, extractorPython), evt => poster.PostAsync(evt));
            trigger = TriggerListener.Build(triggerUrls, handler, extractHandler);
            _ = trigger.RunAsync();
            Console.WriteLine($"resync trigger listening on {triggerUrls}");
        }

        Console.WriteLine($"runner watching {package} on {target} ({platform}) every {interval}s");
        while (true)
        {
            try
            {
                var outcome = runner.RunOnce(force: false);
                if (outcome.Emitted) Console.WriteLine($"emitted build {outcome.Build}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"tick error: {ex.Message}");
            }
            await Task.Delay(interval * 1000);
        }
    }
}
