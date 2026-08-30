using System.ComponentModel;
using System.Diagnostics;

namespace EggIncognito.Core.Services.Devices;

public interface IProcessRunner {
    Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct);

    Task<ProcessBytesResult> RunBytesAsync(string exe, string[] args, CancellationToken ct) =>
        Task.FromResult(new ProcessBytesResult(-1, [], $"{exe}: raw stdout is not supported by this process runner"));
}

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

public sealed record ProcessBytesResult(int ExitCode, byte[] Stdout, string Stderr);

public sealed class ProcessRunner : IProcessRunner {
    public async Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) {
        var psi = new ProcessStartInfo(exe) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        try {
            using var p = Process.Start(psi)!;
            try {
                var outTask = p.StandardOutput.ReadToEndAsync(ct);
                var errTask = p.StandardError.ReadToEndAsync(ct);
                await p.WaitForExitAsync(ct);
                return new ProcessResult(p.ExitCode, await outTask, await errTask);
            } catch (OperationCanceledException) {
                return new ProcessResult(-1, "", $"{exe} canceled (timeout or shutdown){KillNote(p)}");
            }
        } catch (Exception ex) {
            return new ProcessResult(-1, "", ex.Message);
        }
    }

    public async Task<ProcessBytesResult> RunBytesAsync(string exe, string[] args, CancellationToken ct) {
        var psi = new ProcessStartInfo(exe) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        try {
            using var p = Process.Start(psi)!;
            try {
                using var buffer = new MemoryStream();
                var errTask = p.StandardError.ReadToEndAsync(ct);
                await p.StandardOutput.BaseStream.CopyToAsync(buffer, ct);
                await p.WaitForExitAsync(ct);
                return new ProcessBytesResult(p.ExitCode, buffer.ToArray(), await errTask);
            } catch (OperationCanceledException) {
                return new ProcessBytesResult(-1, [], $"{exe} canceled (timeout or shutdown){KillNote(p)}");
            }
        } catch (Exception ex) {
            return new ProcessBytesResult(-1, [], ex.Message);
        }
    }

    private static string KillNote(Process p) {
        try {
            p.Kill(true);
            return "";
        } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) {
            return $"; kill failed: {ex.Message}";
        }
    }
}
