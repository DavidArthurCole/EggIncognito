using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

// Captures a live particle effect off a jailbroken iPhone over ssh, in-process via the IProcessRunner seam.
// The universe hatchery's floating particles are data-driven and not statically extractable (the binding-wall
// memory), so we observe them at runtime: stage the frida hook on the phone, run it against the egginc process
// while the farm is on screen, pull the NDJSON log it writes, and summarize via ParticleCaptureModel.
//
// Requires frida-server running on the phone. The hook resolves addParticle by SYMBOL at runtime (the device
// build differs from the static-analysis fixture), so this works without a fixed offset.
//
// ssh creds reuse the iOS host wiring (DeviceCapture:Ios / DeviceUpdate:Ios fallback), same as IosBinaryPuller.
// Returns a parsed Model; null on any ssh/frida failure so the endpoint degrades cleanly. Never throws.
public sealed class IosParticleCapturer(
    IProcessRunner runner, string sshHost, string sshPort, string sshKeyPath, string localScriptPath)
{
    private const string RemoteScript = "/var/root/particle-capture.js";
    private const string RemoteLog = "/var/root/particle-capture.ndjson";
    private const string ProcessName = "Egg, Inc.";

    public async Task<ParticleCaptureModel.Model?> CaptureAsync(CancellationToken ct)
    {
        if (!File.Exists(localScriptPath)) return null;

        // stage the hook on the phone.
        var push = await runner.RunAsync("scp",
            ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             localScriptPath, $"root@{sshHost}:{RemoteScript}"], ct);
        if (push.ExitCode != 0) return null;

        // run frida against the running egginc process. -n attaches by process name; the script self-detaches
        // after its capture window + exits, so frida returns. A leading rm clears any stale log.
        // No --runtime=v8: frida 17.x is QuickJS-only and the v8 flag faults the agent (connection-closed +
        // the app's anti-tamper-looking crash). Verified on-device 2026-06-29.
        var run = await Ssh(
            $"rm -f {RemoteLog}; frida -U -n {Quote(ProcessName)} -l {RemoteScript} -q 2>&1; " +
            $"echo __frida_exit_$?", ct);
        // frida returning non-zero is not fatal on its own (it can exit oddly after detach); the log is the
        // source of truth. Only bail if the log never appeared.

        var local = Path.Combine(Path.GetTempPath(), $"egi-particle-{Guid.NewGuid():N}.ndjson");
        try
        {
            var pull = await runner.RunAsync("scp",
                ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
                 $"root@{sshHost}:{RemoteLog}", local], ct);
            if (pull.ExitCode != 0 || !File.Exists(local))
            {
                // surface frida's stderr so an empty result is diagnosable (symbol miss, no process, no server).
                var why = string.IsNullOrWhiteSpace(run.Stdout) ? run.Stderr : run.Stdout;
                return new ParticleCaptureModel.Model(false, 0, [], $"no capture log pulled; frida: {Trunc(why)}");
            }
            var ndjson = await File.ReadAllTextAsync(local, ct);
            return ParticleCaptureModel.Parse(ndjson);
        }
        finally
        {
            try { if (File.Exists(local)) File.Delete(local); } catch { /* best-effort */ }
        }
    }

    private Task<ProcessResult> Ssh(string remoteCmd, CancellationToken ct) =>
        runner.RunAsync("ssh",
            ["-p", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{sshHost}", remoteCmd], ct);

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
    private static string Trunc(string s) => s.Length <= 400 ? s : s[..400];
}
