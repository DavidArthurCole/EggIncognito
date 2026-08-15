using EggIncognito.Components.Capture;

namespace EggIncognito.Services.Devices;

public sealed class DeviceWorkbenchState {
    public const string ModeStatus = "status";
    public const string ModeJobs = "jobs";
    public const string ModeCapture = "capture";
    public const string ModeBinaries = "binaries";
    public const string ModeConfig = "config";

    public string? SelectedId { get; set; }
    public string Mode { get; set; } = ModeStatus;
    public HashSet<long> Expanded { get; } = [];
    public CaptureViewState Capture { get; } = new();
}
