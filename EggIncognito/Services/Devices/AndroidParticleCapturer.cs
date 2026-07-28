using System.Text;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class AndroidParticleCapturer(AdbDeviceConnection conn, string scriptBody, string? addrOffset = null) {
    private const string RemoteScript = "/data/local/tmp/particle-capture.js";
    private const string RemoteLog = "/data/local/tmp/particle-capture.ndjson";
    private const string PackageName = "com.auxbrain.egginc";

    public async Task<ParticleCaptureModel.Model?> CaptureAsync(CancellationToken ct) {
        try {
            string? staged = await BuildStagedScriptAsync(ct);
            if (staged is null) return null;

            bool pushed = await conn.PushFileAsync(staged, RemoteScript, ct);
            try {
                File.Delete(staged);
            } catch {
            }

            if (!pushed) return null;

            var run = await conn.ShellAsync(
                $"rm -f {RemoteLog}; command -v frida >/dev/null 2>&1 || {{ echo __frida_missing__; exit 0; }}; " +
                $"frida -U -f {PackageName} -l {RemoteScript} -q > {RemoteLog} 2>&1; echo __frida_exit_$?", ct);
            if (run.Stdout.Contains("__frida_missing__", StringComparison.Ordinal)) return null;

            byte[]? ndjson = await conn.PullBytesAsync(RemoteLog, ct);
            if (ndjson is null || ndjson.Length == 0) return null;

            return ParticleCaptureModel.Parse(Encoding.UTF8.GetString(ndjson));
        } catch {
            return null;
        }
    }

    private async Task<string?> BuildStagedScriptAsync(CancellationToken ct) {
        try {
            string prefix = string.IsNullOrWhiteSpace(addrOffset)
                ? ""
                : $"const addrOffset = '{addrOffset.Trim()}';\n";
            string staged = Path.Combine(Path.GetTempPath(), $"egi-frida-staged-{Guid.NewGuid():N}.js");
            await File.WriteAllTextAsync(staged, prefix + scriptBody, ct);
            return staged;
        } catch {
            return null;
        }
    }
}
