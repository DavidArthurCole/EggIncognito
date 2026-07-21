using EggIncognito.Services;
using Google.Protobuf;

namespace EggIncognito.Capture;


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

   
   
   
   
   
    public sealed record DecodeResult(
        string? Json, string? JsonRaw, string? Type, bool Known, bool Ack = false, string? Text = null);

    public string? KnownResponseType(string path) =>
        _responseTypes.TryGetValue(path, out var t) ? t : null;

    public string? KnownRequestType(string path) =>
        _requestTypes.TryGetValue(path, out var t) ? t : null;

   
    private static DecodeResult Result(string? rawJson, string? type, bool known) =>
        rawJson is null
            ? new(null, null, type, known)
            : new(Redactor.Redact(rawJson), rawJson, type, known);

   
   
    public DecodeResult DecodeResponse(string path, string responseB64)
    {
        byte[] respBytes;
        try { respBytes = ProtoFraming.FromBase64Loose(responseB64); }
        catch { return new(null, null, null, false); }

        var knownType = KnownResponseType(path);




        byte[]? inner = null;
        try
        {
            var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(respBytes);
            inner = outer.Compressed
                ? ProtoFraming.Decompress(outer.Message.ToByteArray())
                : outer.Message.ToByteArray();
        }
        catch { }

        if (knownType is not null)
        {
            var direct = ScoreKnown(knownType, respBytes);
            var wrapped = inner is { Length: > 0 } ? ScoreKnown(knownType, inner) : (score: 0, json: (string?)null);
            var best = wrapped.score > direct.score ? wrapped : direct;
            if (best.json is not null)
                return Result(best.json, knownType, known: true);
        }
        else
        {
            if (inner is { Length: > 0 })
            {
                var det = EndpointExtractor.AutoDetect(inner);
                if (det.json is not null)
                    return Result(det.json, det.typeName, known: false);
            }
            var detRaw = EndpointExtractor.AutoDetect(respBytes);
            if (detRaw.json is not null)
                return Result(detRaw.json, detRaw.typeName, known: false);
        }

        var text = AsPrintableText(respBytes);
        var isAck = _rawResponsePaths.Contains(path);
        if (text is not null)
            return new(null, null, isAck ? "acknowledgement" : "text", Known: isAck, Ack: isAck, Text: text);
        if (isAck)
            return new(null, null, "acknowledgement", Known: true, Ack: true);
        return new(null, null, null, false);
    }



    private static (int score, string? json) ScoreKnown(string knownType, byte[] data)
    {
        try
        {
            var msg = EndpointExtractor.ParseByTypeName(knownType, data);
            if (msg is null) return (0, null);
            var json = JsonFormatter.Default.Format(msg);
            var fieldScore = json.Count(c => c == ':');
            var exact = msg.ToByteArray().AsSpan().SequenceEqual(data);
            return (exact ? 1000 + fieldScore : fieldScore, ProtoJson.PrettyPrint(json));
        }
        catch
        {
            return (0, null);
        }
    }

   
   
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
