using EggIncognito.Core.Services;

namespace EggIncognito.Bot;

public sealed record StatusSnapshot(
    string Mode,
    bool CanCapture,
    bool CanWrite,
    string CaptureState,
    bool CaptureRunning,
    int FlowsCaptured,
    int DeviceCount,
    long BytesCaptured,
    bool DbEnabled,
    bool SigningReady,
    TimeSpan Uptime,
    BuildInfo Build,
    int EndpointsOk,
    int EndpointsEmpty,
    int EndpointsMissing);
