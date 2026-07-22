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
    EggIncognito.Services.IEndpointWriteObserver? WriteObserver = null) {
    public string HarFileName() {
        var name = "session";
        if (!string.IsNullOrEmpty(Label))
            name += "_" + SanitizeRegex().Replace(Label, "_");
        if (!string.IsNullOrEmpty(Eid) &&
            System.Text.RegularExpressions.Regex.IsMatch(Eid, @"^EI\d{16,}$")) {
            name += "_" + Eid;
        }

        return name + ".har";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[^A-Za-z0-9._-]")]
    private static partial System.Text.RegularExpressions.Regex SanitizeRegex();
}
