using System.Text;

namespace EggIncognito.Core.Services.Devices;

public sealed class IosParticleCapturer(SshDeviceConnection conn, string scriptBody, string? addrOffset = null) {
    private const string RemoteScript = "/var/root/particle-capture.js";
    private const string RemoteLog = "/var/root/particle-capture.ndjson";
    private const string ProcessName = "Egg, Inc.";

    public async Task<ParticleCaptureModel.Model?> CaptureAsync(CancellationToken ct) {
        string? staged = await ParticleScript.BuildStagedAsync(scriptBody, addrOffset, ct);
        if (staged is null) return null;

        bool pushed = await conn.PushFileAsync(staged, RemoteScript, ct);
        DeviceShell.TryDelete(staged);

        if (!pushed) return null;

        var run = await conn.ShellAsync(
            $"rm -f {RemoteLog}; frida -U -n {DeviceShell.Quote(ProcessName)} -l {RemoteScript} -q 2>&1; " +
            $"echo __frida_exit_$?", ct);

        byte[]? ndjson = await conn.PullBytesAsync(RemoteLog, ct);
        if (ndjson is null) {
            string why = string.IsNullOrWhiteSpace(run.Stdout) ? run.Stderr : run.Stdout;
            return new ParticleCaptureModel.Model(false, 0, [], $"no capture log pulled; frida: {Trunc(why)}");
        }

        return ParticleCaptureModel.Parse(Encoding.UTF8.GetString(ndjson));
    }

    private static string Trunc(string s) => s.Length <= 400 ? s : s[..400];
}
