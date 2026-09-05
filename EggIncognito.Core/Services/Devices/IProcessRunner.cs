using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace EggIncognito.Core.Services.Devices;

public interface IProcessRunner {
    Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct);

    Task<ProcessBytesResult> RunBytesAsync(string exe, string[] args, CancellationToken ct) =>
        Task.FromResult(new ProcessBytesResult(-1, [], $"{exe}: raw stdout is not supported by this process runner"));

    Task<ProcessHandle> StartAsync(string exe, string[] args, CancellationToken ct) =>
        throw new NotSupportedException($"{exe}: streaming stdout is not supported by this process runner");
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

    public Task<ProcessHandle> StartAsync(string exe, string[] args, CancellationToken ct) {
        var psi = new ProcessStartInfo(exe) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        Process p;
        try {
            p = Process.Start(psi) ?? throw new InvalidOperationException($"{exe} did not start");
        } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException) {
            return Task.FromResult(ProcessHandle.Failed(ex.Message));
        }

        var stderr = new StderrTail();
        var drain = stderr.DrainAsync(p.StandardError);
        var registration = ct.Register(() => TryKill(p));
        var exited = ExitedAsync(p, drain);

        return Task.FromResult(new ProcessHandle(p.StandardOutput.BaseStream, exited, stderr.Snapshot, async () => {
            await registration.DisposeAsync();
            TryKill(p);
            await WaitBoundedAsync(exited, TimeSpan.FromSeconds(5));
            p.Dispose();
        }));
    }

    private static async Task<int> ExitedAsync(Process p, Task drain) {
        await p.WaitForExitAsync(CancellationToken.None);
        await WaitBoundedAsync(drain, TimeSpan.FromSeconds(2));
        return p.ExitCode;
    }

    private static async Task<bool> WaitBoundedAsync(Task task, TimeSpan limit) {
        try {
            await task.WaitAsync(limit, CancellationToken.None);
            return true;
        } catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) {
            return false;
        }
    }

    private static bool TryKill(Process p) {
        try {
            if (!p.HasExited) p.Kill(true);
            return true;
        } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) {
            return false;
        }
    }

    private sealed class StderrTail {
        private const int Keep = 4096;
        private readonly Lock _gate = new();
        private readonly StringBuilder _tail = new();

        public string Snapshot() {
            lock (_gate) return _tail.ToString().Trim();
        }

        public async Task DrainAsync(StreamReader reader) {
            char[] buffer = new char[1024];
            try {
                int n;
                while ((n = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None)) > 0) Append(buffer, n);
            } catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) {
                Append(ex.Message.ToCharArray(), ex.Message.Length);
            }
        }

        private void Append(char[] chars, int count) {
            lock (_gate) {
                _tail.Append(chars, 0, count);
                if (_tail.Length > Keep) _tail.Remove(0, _tail.Length - Keep);
            }
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
