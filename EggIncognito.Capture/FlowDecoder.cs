using EggIncognito.Services;
using Google.Protobuf;

namespace EggIncognito.Capture;

// Decodes a captured flow's raw base64 into readable JSON for the dashboard, in both a redacted
// (safe display) and a raw form. The actual proto-framing heuristic lives in the EggIncognito
// library (EndpointExtractor.DecodeRequestBody) so the dashboard view can never drift from what the
// endpoint pipeline writes. This class is a thin wrapper: load the type maps, call the shared
// decoder, redact.
public sealed class FlowDecoder
{
    private readonly IReadOnlyDictionary<string, string> _responseTypes;
    private readonly IReadOnlyDictionary<string, string> _requestTypes;
    private readonly HashSet<string> _requestWrapped;

    public FlowDecoder(string repoRoot)
    {
        _responseTypes = EndpointExtractor.LoadEndpointTypes(repoRoot);
        _requestTypes = EndpointExtractor.LoadRequestTypes(repoRoot);
        _requestWrapped = EndpointExtractor.LoadRequestWrapped(repoRoot);
    }

    //   Json    - redacted JSON for safe display (PII tokenized, same as written to endpoints)
    //   JsonRaw - the unredacted JSON (shown only when the UI redaction setting is Off)
    //   Type    - the resolved proto type name (yaml-mapped or auto-detected), or null
    //   Known   - true when the type came from routes.yaml (an endpoint we already understand)
    public sealed record DecodeResult(string? Json, string? JsonRaw, string? Type, bool Known);

    public string? KnownResponseType(string path) =>
        _responseTypes.TryGetValue(path, out var t) ? t : null;

    public string? KnownRequestType(string path) =>
        _requestTypes.TryGetValue(path, out var t) ? t : null;

    // Pair a raw (unredacted) JSON string with its redacted copy.
    private static DecodeResult Result(string? rawJson, string? type, bool known) =>
        rawJson is null
            ? new(null, null, type, known)
            : new(Redactor.Redact(rawJson), rawJson, type, known);

    // Decode the response body (base64 of the on-the-wire AuthenticatedMessage) to JSON + type.
    public DecodeResult DecodeResponse(string path, string responseB64)
    {
        try
        {
            var respBytes = ProtoFraming.FromBase64Loose(responseB64);
            var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(respBytes);
            var inner = outer.Compressed
                ? EndpointExtractor.Decompress(outer.Message.ToByteArray())
                : outer.Message.ToByteArray();

            var knownType = KnownResponseType(path);
            if (knownType is not null)
            {
                var msg = EndpointExtractor.ParseByTypeName(knownType, inner);
                if (msg is not null)
                    return Result(EndpointExtractor.PrettyPrint(JsonFormatter.Default.Format(msg)), knownType, known: true);
            }

            var det = EndpointExtractor.AutoDetect(inner);
            return Result(det.json, det.typeName, known: false);
        }
        catch
        {
            return new(null, null, null, false);
        }
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
