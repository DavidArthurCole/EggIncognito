namespace EggIncognito.Capture;

// One captured flow for the live dashboard. Id/Timestamp assigned by the hub at publish time.
public sealed record DashboardFlow(
    long Id,
    string Timestamp,
    string Path,
    string Method,
    int Status,
    string? RequestJson,
    string? ResponseJson,
    string ResponseB64,
    string? RequestDataB64,
    // Proto type names. Known = both sides yaml-mapped (endpoint we fully understand).
    string? RequestType = null,
    string? ResponseType = null,
    bool Known = false,
    // Extractor outcome: "wrote"|"upd"|"diff"|"same"|"loss"|"".
    string Outcome = "",
    // For a "diff" outcome: git-style line counts of the staged change vs the existing endpoint.
    int DiffAdded = 0,
    int DiffRemoved = 0,
    // Unredacted copies; shown only when UI redaction is Off.
    string? RequestJsonRaw = null,
    string? ResponseJsonRaw = null,
    // Full URL including path params (EID etc) the body-only view hides.
    string Url = "",
    // Redacted/raw header pairs; same model as JSON bodies.
    IReadOnlyList<DashboardHeader>? RequestHeaders = null,
    IReadOnlyList<DashboardHeader>? ResponseHeaders = null,
    IReadOnlyList<DashboardHeader>? RequestHeadersRaw = null,
    IReadOnlyList<DashboardHeader>? ResponseHeadersRaw = null,
    // True when the response is a rawResponse (plain-text ack); UI labels it instead of hex view.
    bool ResponseIsAck = false,
    // Plain-text body for non-protobuf responses; null otherwise.
    string? ResponseText = null,
    // True once the user has saved this flow as an endpoint via the dashboard. Persists on the buffered
    // flow so a dashboard refresh does not re-prompt to save the same capture.
    bool Saved = false,
    // Live rinfo (clientVersion/build/platform) from the request. null when none present.
    EggIncognito.Services.RinfoHarvester.ObservedVersion? Observed = null);

// One header for the dashboard. `Sensitive` marks a value that was redacted, so the UI can blur the
// raw copy in blur mode, matching the body redaction model.
public sealed record DashboardHeader(string Name, string Value, bool Sensitive = false);
