using System.Globalization;
using System.Text.RegularExpressions;

namespace EggIncognito.Capture;

public sealed partial record CaptureOptions(
    int Port,
    int DashboardPort,
    string? Eid,
    string? Label,
    bool Overwrite,
    bool Verbose,
    bool NoDashboard,
    bool NoOpen,
    bool ForceOpen) {
    public static CaptureOptions Parse(string[] args) => new(
        Port: GetIntOption(args, "--port") ?? 8080,
        DashboardPort: GetIntOption(args, "--dashboard-port") ?? 8090,
        Eid: GetOption(args, "--eid") ?? Environment.GetEnvironmentVariable("EGG_INC_EID"),
        Label: GetOption(args, "--label"),
        Overwrite: args.Contains("--overwrite"),
        Verbose: args.Contains("--verbose") || args.Contains("-v"),
        NoDashboard: args.Contains("--no-dashboard"),
        NoOpen: args.Contains("--no-open"),
        ForceOpen: args.Contains("--open"));


    public string HarFileName() {
        var name = "session";
        if (!string.IsNullOrEmpty(Label)) name += "_" + Sanitize(Label);
        if (!string.IsNullOrEmpty(Eid) && EidRegex().IsMatch(Eid)) name += "_" + Eid;
        return name + ".har";
    }

    private static string Sanitize(string s) => SanitizeRegex().Replace(s, "_");

    private static string? GetOption(string[] args, string name) {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int? GetIntOption(string[] args, string name) =>
        int.TryParse(GetOption(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    [GeneratedRegex(@"[^A-Za-z0-9._-]")]
    private static partial Regex SanitizeRegex();

    [GeneratedRegex(@"^EI\d{16,}$")]
    private static partial Regex EidRegex();
}
