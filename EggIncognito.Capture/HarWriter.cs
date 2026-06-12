using System.Text;
using System.Text.Json;

namespace EggIncognito.Capture;

// Accumulates captured flows and writes a HAR 1.2 file whose shape is exactly what EndpointExtractor
// reads back:
//   log.entries[].request.{method, url, postData.params[{name,value}]}
//   log.entries[].response.{status, content.{text, encoding}}
// The response body text is the base64 of the raw protobuf, exactly as the auxbrain API returns it
// over the wire and as EndpointExtractor expects in content.text. No content.encoding: the extractor
// reads content.text as the literal base64-protobuf string, so setting encoding="base64" would tell it
// to base64-decode the text first and double-decode the body. The request `data` value is emitted as a
// form param so ReadRequestData picks it up. Re-importing the produced HAR via RunFromHar must be a
// no-op, the round-trip guarantee the capture path relies on.
public sealed class HarWriter
{
    // Add runs on the proxy flow thread while ToHar/Save run on the consumer thread,
    // so every _entries touch goes through _gate.
    private readonly object _gate = new();
    private readonly List<object> _entries = [];

    public int Count { get { lock (_gate) return _entries.Count; } }

    public void Add(CapturedFlow flow)
    {
        var requestParams = flow.RequestDataB64 is null
            ? Array.Empty<object>()
            : [new { name = "data", value = flow.RequestDataB64 }];

        var entry = new
        {
            request = new
            {
                method = flow.Method,
                url = flow.Url,
                headers = HarHeaders(flow.RequestHeaders),
                postData = new
                {
                    mimeType = "application/x-www-form-urlencoded",
                    @params = requestParams,
                },
            },
            response = new
            {
                status = flow.Status,
                headers = HarHeaders(flow.ResponseHeaders),
                content = new
                {
                    text = flow.ResponseBodyB64,
                },
            },
        };

        lock (_gate) _entries.Add(entry);
    }

    // HAR headers[] shape: [{ name, value }]. Raw values - the HAR is the durable capture artifact,
    // gitignored and may contain player data, so it keeps the unredacted headers like it keeps the
    // unredacted bodies. Display-time redaction happens in the dashboard, not here.
    private static object[] HarHeaders(IReadOnlyList<HttpHeader>? headers) =>
        headers is null ? [] : [.. headers.Select(h => new { name = h.Name, value = h.Value })];

    public string ToHar()
    {
        // Snapshot under the gate so serialization never enumerates a list mid-Add.
        object[] entries;
        lock (_gate) entries = [.. _entries];

        var har = new
        {
            log = new
            {
                version = "1.2",
                creator = new { name = "EggIncognito.Capture", version = "1.0" },
                entries,
            },
        };
        return JsonSerializer.Serialize(har, new JsonSerializerOptions { WriteIndented = true });
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToHar(), new UTF8Encoding(false));
    }
}
