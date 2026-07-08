using System.Text;
using System.Text.Json;

namespace EggIncognito.Capture;

// Accumulates captured flows and writes a HAR 1.2 file whose shape is exactly what EndpointExtractor
// reads back:
//   log.entries[].request.{method, url, postData.params[{name,value}]}
//   log.entries[].response.{status, content.{text, encoding}}
// No content.encoding: the extractor reads content.text as the literal base64-protobuf string, so
// setting encoding="base64" would double-decode the body. Re-importing the produced HAR via RunFromHar must be a no-op.
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

    // HAR headers[] shape: [{ name, value }]. Raw values: the HAR keeps unredacted headers/bodies as
    // the durable capture artifact; display-time redaction happens in the dashboard, not here.
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
