using System.Text.RegularExpressions;
using EggIncognito.Services;

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
    public string HarFileName() {
        string name = "session";
        if (!string.IsNullOrEmpty(Label))
            name += "_" + SanitizeRegex().Replace(Label, "_");
        if (!string.IsNullOrEmpty(Eid) &&
            MyRegex().IsMatch(Eid)) {
            name += "_" + Eid;
        }

        return name + ".har";
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]")]
    private static partial Regex SanitizeRegex();

    [GeneratedRegex(@"^EI\d{16,}$")]
    private static partial Regex MyRegex();
}
