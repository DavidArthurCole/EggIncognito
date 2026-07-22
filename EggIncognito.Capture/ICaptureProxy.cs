namespace EggIncognito.Capture;

public sealed record HttpHeader(string Name, string Value);


public sealed record CapturedFlow(
    string Url, string Method, int Status, string? RequestDataB64, string ResponseBodyB64,
    IReadOnlyList<HttpHeader>? RequestHeaders = null,
    IReadOnlyList<HttpHeader>? ResponseHeaders = null);

public interface ICaptureProxy : IAsyncDisposable {

    event Action<CapturedFlow>? FlowCaptured;


    event Action<int, string?>? ClientConnected;
    event Action<int, string?>? ClientDisconnected;
    event Action? AuxbrainConnect;
    event Action<string>? DecryptError;
    event Action? TrustRestored;
    event Action<string, bool>? ConnectSeen;



    event Action<string>? Trace;
    bool Verbose { get; set; }


    bool FreshCa { get; }

    string? RootThumbprint { get; }


    Task StartAsync(int port, string caPath, CancellationToken ct);


    Task StopAsync();
}
