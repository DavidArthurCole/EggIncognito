namespace EggIncognito.Capture;


public enum CertState { Waiting, Untrusted, Trusted }


public sealed record DeviceInfo(
    string Ip,
    string? Hostname,
    int ActiveConnections,
    string FirstSeen,
    string LastSeen,
    string? Os,
    string? GameVersion,
   
    bool Online = true,
    int TotalConnections = 0);

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
   
   
    bool Running = false,
    int Port = 0);

public sealed record RememberedDevice(
    string Ip,
    string? Hostname,
    string? Os,
    string? GameVersion,
    string FirstSeen,
    string LastSeen,
    int TotalConnections);

public sealed record CaptureEvent(string Kind, string Message, string Timestamp);

public sealed record CaptureEnvelope(string Kind, DashboardFlow? Flow, CaptureStats? Stats, CaptureEvent? Event);
