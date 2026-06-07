namespace EggIncognito.Tooling.Dashboard;

// One captured flow, shaped for the live dashboard. Carries both the decoded-for-display JSON
// (so the browser shows readable proto, not base64) and the raw base64 (so a flow can be
// re-saved as a fixture). Id is a monotonic counter assigned by the hub; Timestamp is stamped
// at publish time (the workflow runtime forbids Date.Now in some contexts, but this is a normal
// app, so DateTime.Now here is fine).
public sealed record DashboardFlow(
    long Id,
    string Timestamp,
    string Path,
    string Method,
    int Status,
    string? RequestJson,        // redacted (safe display)
    string? ResponseJson,       // redacted (safe display)
    string ResponseB64,
    string? RequestDataB64,
    // Proto type names for display. *Type is the decoded type (yaml-mapped or auto-detected).
    // Known = both request and response resolved to yaml-mapped types (a fixture we already know),
    // so the UI can show types + a "known" state instead of the Save-as-fixture action.
    string? RequestType = null,
    string? ResponseType = null,
    bool Known = false,
    // Fixture-write outcome from the extractor: "wrote" | "upd" | "diff" | "same" | "loss" | "" .
    // Surfaced in the UI so the console does not need to print capture/diff/loss lines.
    string Outcome = "",
    // For a "diff" outcome: git-style line counts of the staged change vs the existing fixture.
    int DiffAdded = 0,
    int DiffRemoved = 0,
    // Unredacted JSON, shown only when the UI redaction setting is Off. Kept separate so the
    // default view stays redacted.
    string? RequestJsonRaw = null,
    string? ResponseJsonRaw = null,
    // Full request URL, including any query string / path params (e.g. the EID in
    // /ei_srv/subscription_status/EI...). The UI surfaces these params, which the body-only view
    // would otherwise hide.
    string Url = "",
    // Captured HTTP headers, shown behind a default-off "Show headers" option. *Headers are the
    // redacted (safe display) copies; *HeadersRaw are unredacted (shown only when redaction is Off),
    // mirroring how the JSON bodies are carried.
    IReadOnlyList<DashboardHeader>? RequestHeaders = null,
    IReadOnlyList<DashboardHeader>? ResponseHeaders = null,
    IReadOnlyList<DashboardHeader>? RequestHeadersRaw = null,
    IReadOnlyList<DashboardHeader>? ResponseHeadersRaw = null);

// One header for the dashboard. `Sensitive` marks a value that was redacted (so the UI can blur the
// raw copy in "blur" mode), matching the body redaction model.
public sealed record DashboardHeader(string Name, string Value, bool Sensitive = false);
