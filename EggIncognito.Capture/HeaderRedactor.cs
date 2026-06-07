
namespace EggIncognito.Capture;

// Redacts sensitive HTTP header values for dashboard display. The HAR keeps raw headers (durable
// artifact); the dashboard shows redacted values by default and the unredacted copy only when the
// UI redaction mode is Off, mirroring the body-redaction model.
public static class HeaderRedactor
{
    // Header names whose values are secrets / PII and must not be shown in the clear. Matched
    // case-insensitively against the full header name.
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "proxy-authorization",
        "cookie",
        "set-cookie",
        "x-api-key",
        "x-auth-token",
        "x-egg-inc-token",
    };

    public static bool IsSensitive(string name) => Sensitive.Contains(name);

    // Build both the redacted display copy and the raw copy from captured headers.
    public static (IReadOnlyList<DashboardHeader> redacted, IReadOnlyList<DashboardHeader> raw) Build(
        IReadOnlyList<HttpHeader>? headers)
    {
        if (headers is null || headers.Count == 0)
            return (System.Array.Empty<DashboardHeader>(), System.Array.Empty<DashboardHeader>());

        var redacted = new List<DashboardHeader>(headers.Count);
        var raw = new List<DashboardHeader>(headers.Count);
        foreach (var h in headers)
        {
            var sensitive = IsSensitive(h.Name);
            redacted.Add(new DashboardHeader(h.Name, sensitive ? "redacted" : h.Value, sensitive));
            raw.Add(new DashboardHeader(h.Name, h.Value, sensitive));
        }
        return (redacted, raw);
    }
}
