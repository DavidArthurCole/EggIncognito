using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Fake;

public sealed class RefusingProcessRunner : IProcessRunner {
    public const int RefusedExitCode = 126;

    private readonly ConcurrentQueue<string> _attempts = new();

    public IReadOnlyList<string> Attempts => [.. _attempts];

    public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) {
        _attempts.Enqueue(exe);
        return Task.FromResult(new ProcessResult(RefusedExitCode, "",
            $"refused: fake device mode never runs '{exe}'"));
    }
}
