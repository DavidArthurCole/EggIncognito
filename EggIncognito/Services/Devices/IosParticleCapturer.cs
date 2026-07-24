using System.Text;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class IosParticleCapturer(SshDeviceConnection conn, string scriptBody, string? addrOffset = null) {
    private const string RemoteScript = "/var/root/particle-capture.js";
    private const string RemoteLog = "/var/root/particle-capture.ndjson";
    private const string ProcessName = "Egg, Inc.";

    public async Task<ParticleCaptureModel.Model?> CaptureAsync(CancellationToken ct) {
        var staged = await BuildStagedScriptAsync(ct);
        if (staged is null) return null;

        var pushed = await conn.PushFileAsync(staged, RemoteScript, ct);
        try { File.Delete(staged); } catch { }
        if (!pushed) return null;

        var run = await conn.ShellAsync(
            $"rm -f {RemoteLog}; frida -U -n {DeviceShell.Quote(ProcessName)} -l {RemoteScript} -q 2>&1; " +
            $"echo __frida_exit_$?", ct);

        var ndjson = await conn.PullBytesAsync(RemoteLog, ct);
        if (ndjson is null) {
            var why = string.IsNullOrWhiteSpace(run.Stdout) ? run.Stderr : run.Stdout;
            return new ParticleCaptureModel.Model(false, 0, [], $"no capture log pulled; frida: {Trunc(why)}");
        }
        return ParticleCaptureModel.Parse(Encoding.UTF8.GetString(ndjson));
    }

    private async Task<string?> BuildStagedScriptAsync(CancellationToken ct) {
        var prefix = string.IsNullOrWhiteSpace(addrOffset)
            ? ""
            : $"const addrOffset = '{addrOffset.Trim()}';\n";
        var staged = Path.Combine(Path.GetTempPath(), $"egi-frida-staged-{Guid.NewGuid():N}.js");
        await File.WriteAllTextAsync(staged, prefix + scriptBody, ct);
        return staged;
    }

    private static string Trunc(string s) => s.Length <= 400 ? s : s[..400];
}
