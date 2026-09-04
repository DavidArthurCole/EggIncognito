using EggIdentity.UI;
using EggIncognito.Components.Capture;
using EggIncognito.Services.Workbench;

namespace EggIncognito.Services.Devices;

public sealed class DeviceWorkbenchState : WorkbenchStateBase {
    public const string TabOverview = "overview";
    public const string TabScreen = "screen";
    public const string TabRecipes = "recipes";
    public const string TabJobs = "jobs";
    public const string TabCapture = "capture";
    public const string TabBinaries = "binaries";
    public const string FleetId = "fleet";

    private static readonly IReadOnlyList<WorkbenchMode> RawModes = [
        new(TabOverview, "Overview"),
        new(TabScreen, "Screen"),
        new(TabRecipes, "Recipes"),
        new(TabJobs, "Jobs"),
        new(TabCapture, "Capture"),
        new(TabBinaries, "Binaries")
    ];

    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } =
        [.. RawModes.Select(m => (m.Key, m.Label, m.Count))];

    public string? SelectedId { get; set; }
    public HashSet<long> Expanded { get; } = [];
    public CaptureViewState Capture { get; } = new();

    public bool FleetSelected => SelectedId == FleetId;
}
