namespace EggIncognito.Capture;

// A single HTTP header. Lists preserve wire order and allow duplicate names.
public sealed record HttpHeader(string Name, string Value);

// One captured request/response pair, normalized to the fields the endpoint pipeline and HAR need.
// RequestDataB64 is the base64 `data` form value, null for an empty body; ResponseBodyB64 is the
// base64-encoded response body exactly as it came off the wire, the AuthenticatedMessage.
// RequestHeaders/ResponseHeaders are the raw on-the-wire headers; redaction happens downstream at
// display time, mirroring how bodies are handled.
public sealed record CapturedFlow(
    string Url, string Method, int Status, string? RequestDataB64, string ResponseBodyB64,
    IReadOnlyList<HttpHeader>? RequestHeaders = null,
    IReadOnlyList<HttpHeader>? ResponseHeaders = null);

// Thin abstraction over the concrete proxy engine so the pre-1.0 Unobtanium API churn is contained
// behind one seam. The capture command depends only on this.
public interface ICaptureProxy : IAsyncDisposable
{
    // Raised once per completed auxbrain request/response pair on a decrypted flow.
    event Action<CapturedFlow>? FlowCaptured;

    // Connection + health signals that drive the device toast, stats, and cert pill.
    event Action<int, string?>? ClientConnected; // activeCount, realDeviceIp
    event Action<int, string?>? ClientDisconnected; // activeCount, realDeviceIp
    event Action? AuxbrainConnect; // an auxbrain CONNECT was decrypted
    event Action<string>? DecryptError; // a TLS/decrypt error message
    event Action<string, bool>? ConnectSeen; // every CONNECT target seen (host, willDecrypt) - diagnostics

    // Per-flow diagnostic trace (request/response/decrypt-decision lines), emitted only when Verbose is on.
    // Routed to the device-capture log to explain why a decrypted CONNECT produced no captured flow.
    event Action<string>? Trace;
    bool Verbose { get; set; }

    // True if the root CA was freshly created this run; operator must install it once.
    bool FreshCa { get; }
    // The persistent CA's thumbprint, available after StartAsync.
    string? RootThumbprint { get; }

    // Start listening on the given port. Ensures + trusts the root CA and writes it to caPath.
    Task StartAsync(int port, string caPath, CancellationToken ct);

    // Stop listening and untrust the root CA from the local machine if it was added.
    Task StopAsync();
}
