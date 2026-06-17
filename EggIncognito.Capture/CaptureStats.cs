namespace EggIncognito.Capture;

// Inferred state of the capture cert's trust on the connected device. We cannot query the device's
// trust store, so this is derived from observed traffic:
//   Waiting   - no client/device has connected to the proxy yet
//   Untrusted - a device connected and we saw auxbrain CONNECTs, but no auxbrain flow decrypted and a
//               decrypt/TLS error fired - the CA is not trusted
//   Trusted   - at least one auxbrain flow decrypted successfully - the CA is trusted
public enum CertState { Waiting, Untrusted, Trusted }

// Per-connected-device info for the dashboard's device cards. Ip + connection-derived fields are
// always known; Hostname is a best-effort reverse-DNS lookup, null until it resolves. Os and
// GameVersion are derived from the session's most-recently-seen decrypted request's rinfo, and since
// the flow path sees loopback not the device IP, they are only surfaced when a single device is
// connected. Os is a human OS name; GameVersion is the Egg, Inc. client version string.
public sealed record DeviceInfo(
    string Ip,
    string? Hostname,
    int ActiveConnections,
    string FirstSeen,
    string LastSeen,
    string? Os,
    string? GameVersion,
    // Online = connected this session; TotalConnections = lifetime, seeded from remembered value.
    bool Online = true,
    int TotalConnections = 0);

// A point-in-time snapshot of capture statistics for the dashboard. Immutable; the hub rebuilds it
// from its mutable counters under lock and hands out copies.
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
    string CertState,
    // Proxy lifecycle, carried on the stats stream so a freshly-loaded dashboard reflects the live
    // running state immediately, with no race against the one-shot /status poll.
    bool Running = false,
    int Port = 0);

// A device remembered across capture runs, persisted to captures/devices.json. Mirrors the live
// DeviceInfo fields that survive a restart - an IP's identity, last-known OS/version/hostname, and
// when it was seen - so a fresh session can show previously-seen devices as offline cards.
public sealed record RememberedDevice(
    string Ip,
    string? Hostname,
    string? Os,
    string? GameVersion,
    string FirstSeen,
    string LastSeen,
    int TotalConnections);

// A transient toast notification for the dashboard. Kind drives the toast color/icon.
//   deviceConnected, deviceDisconnected, certTrusted, decryptError
public sealed record CaptureEvent(string Kind, string Message, string Timestamp);

// Envelope carried over the single SSE stream. Exactly one payload is non-null; Kind selects it.
//   "flow"   -> Flow
//   "stats"  -> Stats
//   "notice" -> Event
public sealed record CaptureEnvelope(string Kind, DashboardFlow? Flow, CaptureStats? Stats, CaptureEvent? Event);
