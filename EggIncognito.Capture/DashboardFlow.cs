namespace EggIncognito.Capture;

// One captured flow, shaped for the live dashboard. Carries both the decoded-for-display JSON so the
// browser shows readable proto not base64, and the raw base64 so a flow can be re-saved as an
// endpoint. Id is a monotonic counter assigned by the hub; Timestamp is stamped at publish time.
public sealed record DashboardFlow(
    long Id,
    string Timestamp,
    string Path,
    string Method,
    int Status,
    string? RequestJson, // redacted, safe display
    string? ResponseJson, // redacted, safe display
    string ResponseB64,
    string? RequestDataB64,
    // Proto type names for display. *Type is the decoded type, yaml-mapped or auto-detected. Known =
    // both request and response resolved to yaml-mapped types, an endpoint we already know, so the UI
    // can show types + a "known" state instead of the Save-as-endpoint action.
    string? RequestType = null,
    string? ResponseType = null,
    bool Known = false,
    // Endpoint-write outcome from the extractor: "wrote" | "upd" | "diff" | "same" | "loss" | "" .
    // Surfaced in the UI so the console does not need to print capture/diff/loss lines.
    string Outcome = "",
    // For a "diff" outcome: git-style line counts of the staged change vs the existing endpoint.
    int DiffAdded = 0,
    int DiffRemoved = 0,
    // Unredacted JSON, shown only when the UI redaction setting is Off. Kept separate so the default
    // view stays redacted.
    string? RequestJsonRaw = null,
    string? ResponseJsonRaw = null,
    // Full request URL, including any query string or path params such as the EID in
    // /ei_srv/subscription_status/EI.... The UI surfaces these params, which the body-only view would
    // otherwise hide.
    string Url = "",
    // Captured HTTP headers, shown behind a default-off "Show headers" option. *Headers are the
    // redacted safe-display copies; *HeadersRaw are unredacted, shown only when redaction is Off,
    // mirroring how the JSON bodies are carried.
    IReadOnlyList<DashboardHeader>? RequestHeaders = null,
    IReadOnlyList<DashboardHeader>? ResponseHeaders = null,
    IReadOnlyList<DashboardHeader>? RequestHeadersRaw = null,
    IReadOnlyList<DashboardHeader>? ResponseHeadersRaw = null,
    // True when the response is a short non-proto acknowledgement, a rawResponse endpoint: the UI
    // labels it instead of offering a useless hex/binary view.
    bool ResponseIsAck = false,
    // The literal plain-text response body when the response is text rather than protobuf. null for
    // protobuf responses.
    string? ResponseText = null,
    // True once the user has saved this flow as an endpoint via the dashboard. Persists on the buffered
    // flow so a dashboard refresh does not re-prompt to save the same capture.
    bool Saved = false);

// One header for the dashboard. `Sensitive` marks a value that was redacted, so the UI can blur the
// raw copy in blur mode, matching the body redaction model.
public sealed record DashboardHeader(string Name, string Value, bool Sensitive = false);
