using System.Diagnostics;

namespace EggIncognito.Core.Services.Devices;

// Shell-out seam so device probes run without a real device in tests. The default impl spawns the
// process with no shell (ArgumentList, so no quoting/redirection surprises) and captures both streams.
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct);
}

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var outTask = p.StandardOutput.ReadToEndAsync(ct);
            var errTask = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return new ProcessResult(p.ExitCode, await outTask, await errTask);
        }
        catch (Exception ex)
        {
            // exe missing / not on PATH: surface as a non-zero result, never throw into the probe loop.
            return new ProcessResult(-1, "", ex.Message);
        }
    }
}
