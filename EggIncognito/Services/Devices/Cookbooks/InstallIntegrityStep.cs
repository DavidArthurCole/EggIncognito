using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class InstallIntegrityStep(
    VirtualDeviceConfig config,
    ModuleFetcher fetcher,
    IDeviceConnectionFactory connections,
    IProcessRunner runner) : CookbookStep {
    private const string ZygiskOffSql =
        "--sqlite \"REPLACE INTO settings (key,value) VALUES('zygisk',0)\"";
    private static readonly string[] MagiskPaths = [
        "/sbin/magisk", "/debug_ramdisk/magisk",
        "/system/etc/init/magisk/magisk", "/data/adb/magisk/magisk"
    ];
    private const string IntegrityDetect =
        "ls -d /data/adb/modules/*integrity* 2>/dev/null; "
        + "grep -il integrity /data/adb/modules/*/module.prop 2>/dev/null";
    private static readonly TimeSpan AdbTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BootTimeout = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan BootPollInterval = TimeSpan.FromSeconds(5);

    public override string Id => DeviceCookbookIds.InstallIntegrity;
    public override string Title => "Install integrity chain";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Task.FromResult(CookbookStepAvailability.No("installing the integrity chain is android-only"));
        if (!config.IntegrityEnabled)
            return Task.FromResult(CookbookStepAvailability.No("integrity provisioning is not enabled"));

        return Task.FromResult(CookbookStepAvailability.Ready);
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Skipped(lines, "installing the integrity chain is android-only");
        if (!config.IntegrityEnabled)
            return Skipped(lines, "integrity provisioning is not enabled");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        var root = await DeviceRoot.EnsureAsync(conn, runner, target.Target, ct);
        if (!root.Ok)
            return Failed(lines, $"device is not rooted ({root.Detail}); integrity install needs uid=0 (adb root or su)");
        Add($"root: {root.Detail}");

        string? magisk = await MagiskBinaryAsync(conn, root, ct);
        if (magisk is null) {
            return Failed(lines,
                "magisk binary not found; the image has no Magisk bootstrapped into /data "
                + "(rebuild the gapps+magisk image or seed the /data volume). integrity install needs Magisk");
        }

        if (config.IntegrityDisableMagiskZygisk) {
            Add("disabling Magisk built-in Zygisk before installing the zygisk provider");
            await conn.ShellAsync(root.Wrap($"{magisk} {ZygiskOffSql}"), ct);
            if (await RebootAsync(conn, target.Target, Add, ct) is not { } after)
                return Failed(lines, "device did not come back after the Zygisk-toggle reboot");
            root = after;
        }

        foreach (var spec in config.IntegrityModules) {
            var fetched = await fetcher.ResolveAsync(spec, false, ct);
            if (!fetched.Ok || fetched.Bytes is not { } bytes)
                return Failed(lines, $"could not resolve module '{spec.Name}': {fetched.Error ?? "no bytes"}");

            if (spec.Sha256 is { Length: > 0 } expected) {
                string sha = Hashes.Sha256Hex(bytes);
                if (!sha.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    return Failed(lines, $"checksum mismatch for '{spec.Name}' before install: expected {expected}, got {sha}");
            }

            string version = fetched.Version is { Length: > 0 } v ? $" ({v})" : "";
            string origin = fetched.FromCache ? "cache" : "upstream";
            Add($"{spec.Name}{version}: {bytes.Length} bytes from {origin}");

            string remote = $"/data/local/tmp/{spec.Name}.zip";
            string? staged = await PushAsync(conn, bytes, spec.Name, remote, ct);
            if (staged is null) return Failed(lines, $"could not push module '{spec.Name}' to {remote}");

            var install = await conn.ShellAsync(root.Wrap($"{magisk} --install-module {remote}"), ct);
            await conn.ShellAsync(root.Wrap($"rm -f {remote}"), ct);
            if (install.ExitCode != 0) {
                return Failed(lines,
                    $"install of '{spec.Name}' failed: {DeviceParsing.TrimNote(install.Stderr + install.Stdout)}");
            }

            Add($"{spec.Name}: installed");
            if (spec.RebootAfter) {
                if (await RebootAsync(conn, target.Target, Add, ct) is not { } after)
                    return Failed(lines, $"device did not come back after installing '{spec.Name}'");
                root = after;
            }
        }

        bool lastRebooted = config.IntegrityModules is { Count: > 0 } m && m[^1].RebootAfter;
        if (!lastRebooted) {
            if (await RebootAsync(conn, target.Target, Add, ct) is not { } after)
                return Failed(lines, "device did not come back after the final reboot");
            root = after;
        }

        var verify = await conn.ShellAsync(root.Wrap(IntegrityDetect), ct);
        if (verify.Stdout.Trim().Length == 0)
            return Failed(lines, "Integrity-Box module not found under /data/adb/modules after install");

        Add("Integrity-Box module present");
        return Ok(lines, $"installed {config.IntegrityModules.Count} module(s)");
    }

    private static async Task<string?> MagiskBinaryAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        foreach (string p in MagiskPaths) {
            var probe = await conn.ShellAsync(root.Wrap($"[ -x {p} ] && echo yes"), ct);
            if (probe.Stdout.Contains("yes", StringComparison.Ordinal)) return p;
        }

        var onPath = await conn.ShellAsync(root.Wrap("command -v magisk"), ct);
        return onPath.Stdout.Trim().Length > 0 ? "magisk" : null;
    }

    private static async Task<string?> PushAsync(
        IDeviceConnection conn, byte[] bytes, string name, string remote, CancellationToken ct) {
        string local = DeviceShell.NewTempPath($"-{name}.zip");
        try {
            await File.WriteAllBytesAsync(local, bytes, ct);
            return await conn.PushFileAsync(local, remote, ct) ? remote : null;
        } finally {
            DeviceShell.TryDelete(local);
        }
    }

    private async Task<RootAccess?> RebootAsync(IDeviceConnection conn, string serial, Action<string> add, CancellationToken ct) {
        add("rebooting and waiting for boot");
        await Adb(["-s", serial, "reboot"], ct);

        var deadline = DateTimeOffset.UtcNow + BootTimeout;
        while (DateTimeOffset.UtcNow < deadline) {
            await Task.Delay(BootPollInterval, ct);
            await Adb(["connect", serial], ct);
            var boot = await Adb(["-s", serial, "shell", "getprop sys.boot_completed"], ct);
            if (boot.Stdout.Trim() != "1") continue;

            add("boot completed");
            var root = await DeviceRoot.EnsureAsync(conn, runner, serial, ct);
            add($"root after reboot: {root.Detail}");
            return root;
        }

        return null;
    }

    private async Task<ProcessResult> Adb(string[] args, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(AdbTimeout);
        return await runner.RunAsync("adb", args, cts.Token);
    }
}
