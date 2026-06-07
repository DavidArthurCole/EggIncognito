namespace EggIncognito.Capture;

// Inferred state of the capture cert's trust on the connected device. We cannot query the
// device's trust store, so this is derived from observed traffic:
//   Waiting   - no client/device has connected to the proxy yet
//   Untrusted - a device connected and we saw auxbrain CONNECTs, but no auxbrain flow has
//               successfully decrypted (a decrypt/TLS error fired) - the CA is not trusted
//   Trusted   - at least one auxbrain flow decrypted successfully - the CA is trusted
public enum CertState { Waiting, Untrusted, Trusted }

// Per-connected-device info for the dashboard's device cards. Ip + connection-derived fields are
// always known; Hostname is a best-effort reverse-DNS lookup (null until/if it resolves);
// UserAgent is the session's most-recently-seen decrypted User-Agent (see CaptureHub note - it is
// not reliably attributable per-device, so it is only surfaced when a single device is connected).
public sealed record DeviceInfo(
    string Ip,
    string? Hostname,
    int ActiveConnections,
    string FirstSeen,
    string LastSeen,
    string? UserAgent);

// A point-in-time snapshot of capture statistics for the dashboard. Immutable; the hub rebuilds
// it from its mutable counters under lock and hands out copies.
public sealed record CaptureStats(
    int ActiveConnections,
    int DeviceCount,
    IReadOnlyList<DeviceInfo> Devices,
    int CapturedAuxbrain,
    int Passthrough,
    int UniqueEndpoints,
    int DecryptOk,
    int DecryptErrors,
    string? LastError,
    long BytesCaptured,
    string? BiggestEndpoint,
    long BiggestEndpointBytes,
    string CertState);

// A transient notification ("toast") for the dashboard. Kind drives the toast color/icon.
//   deviceConnected, deviceDisconnected, certTrusted, decryptError
public sealed record CaptureEvent(string Kind, string Message, string Timestamp);

// Envelope carried over the single SSE stream. Exactly one payload is non-null; Kind selects it.
//   "flow"   -> Flow
//   "stats"  -> Stats
//   "notice" -> Event
public sealed record CaptureEnvelope(string Kind, DashboardFlow? Flow, CaptureStats? Stats, CaptureEvent? Event);
