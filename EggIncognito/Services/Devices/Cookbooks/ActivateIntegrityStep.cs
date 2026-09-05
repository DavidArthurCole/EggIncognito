using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class ActivateIntegrityStep(
    VirtualDeviceConfig config,
    IDeviceFleet fleet,
    IDeviceConnectionFactory connections) : CookbookStep {
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromMinutes(6);
    private static readonly TimeSpan CheckinWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CheckinPoll = TimeSpan.FromSeconds(10);
    private const string PifLogCommand = "logcat -d -s PIF/Native 2>/dev/null | tail -n 12";

    public override string Id => DeviceCookbookIds.ActivateIntegrity;
    public override string Title => "Activate integrity chain";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Task.FromResult(CookbookStepAvailability.No("activating the integrity chain is android-only"));
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
            return Skipped(lines, "activating the integrity chain is android-only");
        if (!config.IntegrityEnabled)
            return Skipped(lines, "integrity provisioning is not enabled");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        if (!root.Ok)
            return Failed(lines, $"device is not rooted ({root.Detail}); activation needs uid=0");

        var before = await StateAsync(conn, root, ct);
        if (before is null) return Failed(lines, "chain state probe did not run");
        if (!before.BoxPresent)
            return Failed(lines, "Integrity-Box is not installed under /data/adb/modules; run install-integrity first");
        Add($"before: {before.Describe()}");

        Add("running Integrity-Box action.sh (keybox fetch, fingerprint, tricky-store targets, teesim sync)");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ActionTimeout);
        var action = await conn.ShellAsync(root.Wrap($"sh {IntegrityChain.ActionScript} 2>&1"), cts.Token);
        foreach (string line in IntegrityChain.ActionLines(action.Stdout)) Add(line);
        if (action.ExitCode != 0) {
            var keyLog = await conn.ShellAsync(root.Wrap($"tail -n 8 {IntegrityChain.KeyboxLog} 2>/dev/null"), ct);
            return Failed(lines,
                $"action.sh exited {action.ExitCode}; the keybox fetch needs the device online: "
                + DeviceParsing.TrimNote(keyLog.Stdout + action.Stderr));
        }

        var adopt = await conn.ShellAsync(root.Wrap(IntegrityChain.AdoptKeyboxCommand), ct);
        if (!adopt.Stdout.Contains("adopted=1", StringComparison.Ordinal))
            return Failed(lines, "no keybox reached TEESimulator; action.sh did not fetch one (device offline?)");

        var targeted = await conn.ShellAsync(root.Wrap(IntegrityChain.EnsureTargetCommand(target.Package)), ct);
        if (!targeted.Stdout.Contains("targeted=1", StringComparison.Ordinal))
            return Failed(lines, $"could not add {target.Package} to {IntegrityChain.Targets}");
        Add($"{target.Package} listed for attestation");

        var after = await StateAsync(conn, root, ct);
        if (after is null || !after.Activated)
            return Failed(lines, $"chain still inert after action.sh: {after?.Describe() ?? "probe did not run"}");
        Add($"after: {after.Describe()}");

        bool virtualDevice = await IsVirtualAsync(target.Id, ct);
        var reset = await conn.ShellAsync(root.Wrap(IntegrityChain.ResetPlayCommand(virtualDevice)), ct);
        if (!reset.Stdout.Contains("reset=1", StringComparison.Ordinal))
            return Failed(lines, $"play reset did not complete: {DeviceParsing.TrimNote(reset.Stderr + reset.Stdout)}");
        Add(virtualDevice
            ? "play store + gsf cleared; waiting for gms to check in under the spoofed identity"
            : "play store cleared; gms restarted under the spoofed identity");

        if (virtualDevice) {
            string? gsf = await GsfIdentity.WaitAsync(conn, CheckinWait, CheckinPoll, ct);
            if (gsf is null)
                return Failed(lines, $"gms did not check in within {CheckinWait.TotalMinutes:F0} min; no gsf id (device offline?)");
            Add($"gms checked in, gsf id {gsf}");
        }

        var pif = await conn.ShellAsync(PifLogCommand, ct);
        var spoofed = pif.Stdout.Split('\n').Where(l => l.Contains("Spoofing", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf("Spoofing", StringComparison.Ordinal) + "Spoofing ".Length)..].Trim()).ToList();
        Add(spoofed.Count > 0
            ? $"pif injected into gms: {string.Join("; ", spoofed.Distinct())}"
            : "no PIF/Native lines in logcat yet; DroidGuard spawns on demand, re-check after launching the app");

        return Ok(lines, after.Describe());
    }

    private static async Task<IntegrityChainState?> StateAsync(
        IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        var r = await conn.ShellAsync(root.Wrap(IntegrityChain.StateCommand), ct);
        return IntegrityChain.Ran(r.Stdout) ? IntegrityChain.Parse(r.Stdout) : null;
    }

    private async Task<bool> IsVirtualAsync(string deviceId, CancellationToken ct) {
        var entry = (await fleet.EnabledAsync(ct)).FirstOrDefault(d =>
            string.Equals(d.Id, deviceId, StringComparison.Ordinal));
        return entry is not null && DeviceOrigins.IsVirtual(entry.Origin);
    }
}
