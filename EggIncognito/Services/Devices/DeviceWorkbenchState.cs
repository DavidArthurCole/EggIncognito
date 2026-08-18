using EggIncognito.Components.Capture;
using EggIncognito.Services.Workbench;

namespace EggIncognito.Services.Devices;

public sealed class DeviceWorkbenchState : WorkbenchStateBase {
    public const string SectionJobs = "jobs";
    public const string SectionCapture = "capture";
    public const string SectionBinaries = "binaries";

    public override IReadOnlyList<WorkbenchMode> Modes { get; } = [];

    public string? SelectedId { get; set; }
    public HashSet<string> Sections { get; } = [SectionJobs];
    public HashSet<long> Expanded { get; } = [];
    public CaptureViewState Capture { get; } = new();
}
