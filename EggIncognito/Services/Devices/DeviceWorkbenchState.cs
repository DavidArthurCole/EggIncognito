using EggIncognito.Components.Capture;
using EggIncognito.Services.Workbench;

namespace EggIncognito.Services.Devices;

public sealed class DeviceWorkbenchState : WorkbenchStateBase {
    public const string TabJobs = "jobs";
    public const string TabCapture = "capture";
    public const string TabBinaries = "binaries";

    public override IReadOnlyList<WorkbenchMode> Modes { get; } = [
        new(TabJobs, "Jobs"),
        new(TabCapture, "Capture"),
        new(TabBinaries, "Binaries")
    ];

    public string? SelectedId { get; set; }
    public HashSet<long> Expanded { get; } = [];
    public CaptureViewState Capture { get; } = new();
}
