using System.Text.RegularExpressions;
using EggIncognito.Core.Services;

namespace EggIncognito.Capture;

public sealed partial record CaptureSessionOptions(
    int Port,
    string? Eid,
    string? Label,
    bool Overwrite,
    bool Verbose,
    string CapturePath,
    string CaPath,
    bool WriteEndpoints = true,
    IEndpointWriteObserver? WriteObserver = null) {
    public IReadOnlyCollection<string> LiveRoutes { get; init; } = [];

    public CaptureTier Tier { get; init; } = CaptureTier.Full;

    public IReadOnlySet<string> FullDetailRoutes { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public Action<Guid, DashboardFlow>? OnContribution { get; init; }

    public string HarFileName() {
        string name = "session";
        if (!string.IsNullOrEmpty(Label))
            name += "_" + SanitizeRegex().Replace(Label, "_");
        if (!string.IsNullOrEmpty(Eid) &&
            EidPattern.Exact.IsMatch(Eid)) {
            name += "_" + Eid;
        }

        return name + ".har";
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]")]
    private static partial Regex SanitizeRegex();
}
