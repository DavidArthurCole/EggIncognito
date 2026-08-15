using EggIncognito.Components.Capture;
using EggIncognito.Services.Workbench;

namespace EggIncognito.Services.Devices;

public sealed class DeviceWorkbenchState : WorkbenchStateBase {
    public const string ModeStatus = "status";
    public const string ModeJobs = "jobs";
    public const string ModeCapture = "capture";
    public const string ModeBinaries = "binaries";
    public const string ModeConfig = "config";

    public override IReadOnlyList<WorkbenchMode> Modes { get; } = [
        new WorkbenchMode(ModeStatus, "Status"),
        new WorkbenchMode(ModeJobs, "Jobs"),
        new WorkbenchMode(ModeCapture, "Capture"),
        new WorkbenchMode(ModeBinaries, "Binaries"),
        new WorkbenchMode(ModeConfig, "Config")
    ];

    public string? SelectedId { get; set; }
    public HashSet<long> Expanded { get; } = [];
    public CaptureViewState Capture { get; } = new();
}
