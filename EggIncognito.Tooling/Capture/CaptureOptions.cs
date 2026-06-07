using System.Text.RegularExpressions;

namespace EggIncognito.Tooling.Capture;

// Parsed command-line options for the `capture` subcommand. Folds in the args.Contains / Get*Option
// parsing that used to open RunAsync, plus the HAR-filename derivation, so the command method reads
// the decisions off one record instead of poking at the raw string[].
public sealed record CaptureOptions(
    int Port,
    int DashboardPort,
    string? Eid,
    string? Label,
    bool Overwrite,
    bool Verbose,
    bool NoDashboard,
    bool NoOpen,
    bool ForceOpen)
{
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

    // captures/session[_label][_EID].har - keeps the EI\d{16,} convention so the Seeder's EID
    // scrub still applies when the HAR is re-run.
    public string HarFileName()
    {
        var name = "session";
        if (!string.IsNullOrEmpty(Label)) name += "_" + Sanitize(Label);
        if (!string.IsNullOrEmpty(Eid) && Regex.IsMatch(Eid, @"^EI\d{16,}$")) name += "_" + Eid;
        return name + ".har";
    }

    private static string Sanitize(string s) => Regex.Replace(s, @"[^A-Za-z0-9._-]", "_");

    private static string? GetOption(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int? GetIntOption(string[] args, string name) =>
        int.TryParse(GetOption(args, name), out var v) ? v : null;
}
