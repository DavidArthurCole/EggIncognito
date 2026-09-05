namespace EggIncognito.Core.Services.Devices;

public sealed class ProcessHandle(
    Stream stdout,
    Task<int> exited,
    Func<string> stderrTail,
    Func<ValueTask> shutdown) : IAsyncDisposable {
    public Stream Stdout => stdout;
    public Task<int> Exited => exited;
    public Func<string> StderrTail => stderrTail;

    public static ProcessHandle Failed(string note) =>
        new(Stream.Null, Task.FromResult(-1), () => note, () => ValueTask.CompletedTask);

    public ValueTask DisposeAsync() => shutdown();
}
