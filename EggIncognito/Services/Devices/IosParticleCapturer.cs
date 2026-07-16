using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;


public sealed class IosParticleCapturer(
    IProcessRunner runner, string sshHost, string sshPort, string sshKeyPath, string localScriptPath,
    string? addrOffset = null)
{
    private const string RemoteScript = "/var/root/particle-capture.js";
    private const string RemoteLog = "/var/root/particle-capture.ndjson";
    private const string ProcessName = "Egg, Inc.";

    public async Task<ParticleCaptureModel.Model?> CaptureAsync(CancellationToken ct)
    {
        if (!File.Exists(localScriptPath)) return null;

        var staged = await BuildStagedScriptAsync(ct);
        if (staged is null) return null;

        var push = await runner.RunAsync("scp",
            ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             staged, $"root@{sshHost}:{RemoteScript}"], ct);
        try { File.Delete(staged); } catch { }
        if (push.ExitCode != 0) return null;

       
        var run = await Ssh(
            $"rm -f {RemoteLog}; frida -U -n {Quote(ProcessName)} -l {RemoteScript} -q 2>&1; " +
            $"echo __frida_exit_$?", ct);

        var local = Path.Combine(Path.GetTempPath(), $"egi-particle-{Guid.NewGuid():N}.ndjson");
        try
        {
            var pull = await runner.RunAsync("scp",
                ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
                 $"root@{sshHost}:{RemoteLog}", local], ct);
            if (pull.ExitCode != 0 || !File.Exists(local))
            {
                var why = string.IsNullOrWhiteSpace(run.Stdout) ? run.Stderr : run.Stdout;
                return new ParticleCaptureModel.Model(false, 0, [], $"no capture log pulled; frida: {Trunc(why)}");
            }
            var ndjson = await File.ReadAllTextAsync(local, ct);
            return ParticleCaptureModel.Parse(ndjson);
        }
        finally
        {
            try { if (File.Exists(local)) File.Delete(local); } catch { }
        }
    }

   
    private async Task<string?> BuildStagedScriptAsync(CancellationToken ct)
    {
        var body = await File.ReadAllTextAsync(localScriptPath, ct);
        var prefix = string.IsNullOrWhiteSpace(addrOffset)
            ? ""
            : $"const addrOffset = '{addrOffset.Trim()}';\n";
        var staged = Path.Combine(Path.GetTempPath(), $"egi-frida-staged-{Guid.NewGuid():N}.js");
        await File.WriteAllTextAsync(staged, prefix + body, ct);
        return staged;
    }

    private Task<ProcessResult> Ssh(string remoteCmd, CancellationToken ct) =>
        runner.RunAsync("ssh",
            ["-p", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{sshHost}", remoteCmd], ct);

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
    private static string Trunc(string s) => s.Length <= 400 ? s : s[..400];
}
