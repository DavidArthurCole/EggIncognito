using EggIncognito.Capture;

namespace EggIncognito.Tests;

public sealed class FakeCaptureProxy : ICaptureProxy
{
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }
    public int? LastPort { get; private set; }
    public bool ThrowOnStart { get; set; }
    public bool ThrowOnStop { get; set; }

    public event Action<CapturedFlow>? FlowCaptured;
    public event Action<int, string?>? ClientConnected;
#pragma warning disable CS0067
    public event Action<int, string?>? ClientDisconnected;
    public event Action? AuxbrainConnect;
    public event Action<string>? DecryptError;
    public event Action<string, bool>? ConnectSeen;
    public event Action<string>? Trace;
#pragma warning restore CS0067

    public bool Verbose { get; set; }
    public bool FreshCa => false;
    public string? RootThumbprint => "FAKE-THUMB";

    public Task StartAsync(int port, string caPath, CancellationToken ct)
    {
        StartCount++;
        LastPort = port;
        if (ThrowOnStart) throw new InvalidOperationException("fake proxy start failure");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCount++;
        if (ThrowOnStop) throw new InvalidOperationException("fake proxy stop failure");
        return Task.CompletedTask;
    }

    public void EmitFlow(CapturedFlow flow) => FlowCaptured?.Invoke(flow);
    public void EmitConnect(int count, string? ip) => ClientConnected?.Invoke(count, ip);

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}
