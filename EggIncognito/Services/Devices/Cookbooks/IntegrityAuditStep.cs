using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class IntegrityAuditStep(IDeviceConnectionFactory connections) : CookbookStep {
    private const string ZygiskModuleDir = "/data/adb/modules/zygisksu";
    private const string TeesimModuleDir = "/data/adb/modules/teesim";

    private static readonly (string Label, string Command)[] ShellProbes = [
        ("build", "getprop ro.build.fingerprint; getprop ro.product.model; getprop ro.product.cpu.abilist; getprop ro.build.version.sdk"),
        ("boot state", "getprop ro.boot.verifiedbootstate; getprop ro.boot.flash.locked; getprop ro.boot.vbmeta.device_state; getprop ro.debuggable; getprop ro.secure"),
        ("gms", "dumpsys package " + IntegrityChain.GmsPackage + " 2>/dev/null | grep -m1 versionName; dumpsys package " + IntegrityChain.PlayStorePackage + " 2>/dev/null | grep -m1 versionName"),
        ("gsf id", GsfIdentity.AndroidIdQuery),
        ("proxy", "settings get global http_proxy"),
        ("network", "dumpsys connectivity 2>/dev/null | grep -i -E 'validated|captive|NetworkAgentInfo' | head -n 6"),
        ("reach via proxy", "p=$(settings get global http_proxy); "
                            + "echo \"https $(curl -sS -m 10 -x \"http://$p\" -o /dev/null -w '%{http_code}' https://www.google.com/generate_204 2>&1)\"; "
                            + "echo \"http $(curl -sS -m 10 -x \"http://$p\" -o /dev/null -w '%{http_code}' http://connectivitycheck.gstatic.com/generate_204 2>&1)\""),
        ("reach direct", "echo \"https $(curl -sS -m 10 --noproxy '*' -o /dev/null -w '%{http_code}' https://www.google.com/generate_204 2>&1)\""),
        ("checkin log", "logcat -d 2>/dev/null | grep -i -E 'checkin' | tail -n 12"),
        ("pif log", "logcat -d -s PIF/Native PIF/Java PIF:* 2>/dev/null | tail -n 20"),
        ("droidguard log", "logcat -d 2>/dev/null | grep -i -E 'droidguard|snet|attest' | tail -n 15"),
        ("zygisk processes", "ps -A 2>/dev/null | grep -i -E 'zygisk|magisk' | grep -v grep")
    ];

    private const string CompanionProbe = "ps -A 2>/dev/null | grep -c 'zn-zygisk-companion.*playintegrityfix'";

    private static readonly (string Label, string Command)[] RootProbes = [
        ("magisk settings", "/sbin/magisk --sqlite \"SELECT key,value FROM settings\" 2>&1"),
        ("zygisknext status", "grep -m1 ^description= " + ZygiskModuleDir + "/module.prop 2>&1; ls -l " + ZygiskModuleDir + " 2>&1"),
        ("teesim status", "grep -m1 ^description= " + TeesimModuleDir + "/module.prop 2>&1; ls -l " + IntegrityChain.TeesimDir + " 2>&1; cat " + IntegrityChain.TeesimConfig + " 2>&1"),
        ("pif module", "ls -l " + IntegrityChain.PifModuleDir + " 2>&1; cat " + IntegrityChain.PifProp + " 2>&1"),
        ("tricky store", "ls -l " + IntegrityChain.TrickyStoreDir + " 2>&1; cat " + IntegrityChain.Targets + " 2>&1; cat " + IntegrityChain.SecurityPatchFile + " 2>&1"),
        ("integrity-box logs", "ls -lt " + IntegrityChain.LogDir + " 2>&1 | head -n 6; for f in $(ls -t " + IntegrityChain.LogDir + " 2>/dev/null | head -n 2); do echo \"== $f\"; tail -n 15 " + IntegrityChain.LogDir + "/$f; done"),
        ("keybox certs", "grep -c -i '<Certificate' " + IntegrityChain.TeesimKeybox + " 2>&1; grep -c -i '<Certificate' " + IntegrityChain.TrickyKeybox + " 2>&1")
    ];

    public override string Id => DeviceCookbookIds.IntegrityAudit;
    public override string Title => "Integrity audit";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(Platforms.Matches(target.Platform, Platforms.Android)
            ? CookbookStepAvailability.Ready
            : CookbookStepAvailability.No("the integrity audit is android-only"));

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        if (connections.For(context.Target) is not { } conn) return Failed(lines, "no connection for this device");

        foreach (var (label, command) in ShellProbes) await ReportAsync(conn, label, command, Add, ct);

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        Add($"root: {root.Detail}");
        if (root.Ok) {
            foreach (var (label, command) in RootProbes) await ReportAsync(conn, label, root.Wrap(command), Add, ct);
        }

        var pif = await conn.ShellAsync("logcat -d -s PIF/Native 2>/dev/null | grep -c Spoofing", ct);
        bool logged = int.TryParse(pif.Stdout.Trim(), out int n) && n > 0;
        var companion = await conn.ShellAsync(CompanionProbe, ct);
        bool companionLive = int.TryParse(companion.Stdout.Trim(), out int c) && c > 0;
        string? gsf = await GsfIdentity.ReadAsync(conn, ct);
        string injection = logged
            ? "pif logs spoofing into gms"
            : companionLive
                ? "pif companion is running (logs are off, verboseLogs=0), so Zygisk loaded it"
                : "no pif log line and no pif companion process: Zygisk is not loading it";
        string verdict = (logged || companionLive, gsf) switch {
            (false, _) => $"{injection}; DroidGuard sees the real build",
            (true, null) => $"{injection}; gms has no gsf id, check-in has not completed, so Play cannot certify yet. Read 'reach via proxy' and 'checkin log' above",
            (true, _) => $"{injection}; gms is checked in (gsf id {gsf}); a failing verdict is DroidGuard rejecting this device profile"
        };
        Add($"verdict: {verdict}");
        return Ok(lines, verdict);
    }

    private static async Task ReportAsync(
        IDeviceConnection conn, string label, string command, Action<string> add, CancellationToken ct) {
        var r = await conn.ShellAsync(command, ct);
        string[] output = [.. (r.Stdout + "\n" + r.Stderr).Split('\n')
            .Select(l => l.TrimEnd('\r').TrimEnd())
            .Where(l => l.Length > 0)];
        if (output.Length == 0) {
            add($"{label}: (no output, exit {r.ExitCode})");
            return;
        }

        add($"{label}:");
        foreach (string line in output) add("  " + line);
    }
}
