using EggIncognito.Services;
using Google.Protobuf;

namespace EggIncognito.Capture;

// Decodes a captured flow's raw base64 into readable JSON for the dashboard, in both a redacted
// safe-display form and a raw form. The actual proto-framing heuristic lives in
// EndpointExtractor.DecodeRequestBody so the dashboard view can never drift from what the endpoint
// pipeline writes. This class is a thin wrapper: load the type maps, call the shared decoder, redact.
public sealed class FlowDecoder
{
    private readonly IReadOnlyDictionary<string, string> _responseTypes;
    private readonly IReadOnlyDictionary<string, string> _requestTypes;
    private readonly HashSet<string> _requestWrapped;
    private readonly HashSet<string> _rawResponsePaths;

    public FlowDecoder(string contentRoot)
    {
        _responseTypes = EndpointExtractor.LoadEndpointTypes(contentRoot);
        _requestTypes = EndpointExtractor.LoadRequestTypes(contentRoot);
        _requestWrapped = EndpointExtractor.LoadRequestWrapped(contentRoot);
        _rawResponsePaths = new HashSet<string>(
            EndpointExtractor.LoadRawResponses(contentRoot).Keys, StringComparer.Ordinal);
    }

    //   Json/JsonRaw - redacted/unredacted display JSON
    //   Type         - resolved proto type name (yaml-mapped or auto-detected), or null
    //   Known        - type came from routes.yaml
    //   Ack          - rawResponse endpoint (plain-text ack, not proto)
    //   Text         - plain-text body; null for proto responses
    public sealed record DecodeResult(
        string? Json, string? JsonRaw, string? Type, bool Known, bool Ack = false, string? Text = null);

    public string? KnownResponseType(string path) =>
        _responseTypes.TryGetValue(path, out var t) ? t : null;

    public string? KnownRequestType(string path) =>
        _requestTypes.TryGetValue(path, out var t) ? t : null;

    // Pair a raw unredacted JSON string with its redacted copy.
    private static DecodeResult Result(string? rawJson, string? type, bool known) =>
        rawJson is null
            ? new(null, null, type, known)
            : new(Redactor.Redact(rawJson), rawJson, type, known);

    // Decode the response body to JSON + type. The body is base64 of the response bytes; for the
    // protobuf endpoints those bytes are an AuthenticatedMessage, but the log/data endpoints reply with
    // a short plain-text ack, which we surface as text not a hex dump.
    public DecodeResult DecodeResponse(string path, string responseB64)
    {
        byte[] respBytes;
        try { respBytes = ProtoFraming.FromBase64Loose(responseB64); }
        catch { return new(null, null, null, false); }

        // Try the protobuf framing first.
        try
        {
            var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(respBytes);
            var inner = outer.Compressed
                ? ProtoFraming.Decompress(outer.Message.ToByteArray())
                : outer.Message.ToByteArray();

            var knownType = KnownResponseType(path);
            if (knownType is not null)
            {
                var msg = EndpointExtractor.ParseByTypeName(knownType, inner);
                if (msg is not null)
                    return Result(ProtoJson.PrettyPrint(JsonFormatter.Default.Format(msg)), knownType, known: true);
            }

            var det = EndpointExtractor.AutoDetect(inner);
            if (det.json is not null)
                return Result(det.json, det.typeName, known: false);
        }
        catch { /* not an AuthenticatedMessage - fall through to the text/ack handling below */ }

        // Not protobuf. If the body is printable text, show it verbatim. rawResponse endpoints are
        // additionally flagged as acks.
        var text = AsPrintableText(respBytes);
        var isAck = _rawResponsePaths.Contains(path);
        if (text is not null)
            return new(null, null, isAck ? "acknowledgement" : "text", Known: isAck, Ack: isAck, Text: text);
        if (isAck)
            return new(null, null, "acknowledgement", Known: true, Ack: true);
        return new(null, null, null, false);
    }

    // The string form of a short, fully-printable body (no control chars), else null. Caps length so a
    // large binary blob that happens to be printable is not treated as text.
    private static string? AsPrintableText(byte[] bytes)
    {
        if (bytes.Length is 0 or > 256) return null;
        foreach (var b in bytes)
        {
            if (b is < 0x20 and not ((byte)'\t' or (byte)'\r' or (byte)'\n') || b == 0x7f) return null;
        }
        try { return System.Text.Encoding.UTF8.GetString(bytes).Trim(); }
        catch { return null; }
    }

    // Decode the request `data` base64 to JSON + type via the shared library decoder.
    public DecodeResult DecodeRequest(string path, string? requestDataB64)
    {
        var knownType = KnownRequestType(path);
        if (string.IsNullOrEmpty(requestDataB64)) return new(null, null, knownType, false);
        try
        {
            var bytes = ProtoFraming.FromBase64Loose(requestDataB64);
            var wrapped = _requestWrapped.Contains(path);
            var (json, type) = EndpointExtractor.DecodeRequestBody(knownType, wrapped, bytes);
            return Result(json, type, known: knownType is not null && json is not null);
        }
        catch
        {
            return new(null, null, knownType, false);
        }
    }
}
