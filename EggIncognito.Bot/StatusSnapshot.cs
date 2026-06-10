using EggIncognito.Services;

namespace EggIncognito.Bot;

// Everything the status/verify/endpoints embeds need, captured at one instant. Built by
// StatusSnapshotFactory from the live services; consumed by the PURE BotEmbeds builders so those can
// be unit-tested without any running services or gateway. Mode is a string ("Local"/"Hosted") because
// the AppMode enum lives in the web project, which this library does not reference.
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
