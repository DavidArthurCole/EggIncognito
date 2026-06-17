using System.Text;

namespace EggIncognito.Services;

// Reads mitmproxy .mitm flow files into the same (url, method, status, requestData, responseBody)
// tuples the HAR path produces, so both feed EndpointExtractor.ProcessFlow identically. A .mitm file
// is a sequence of top-level tnetstring dicts, one per flow; each HTTP flow holds request + response
// sub-dicts. Request/response string fields are stored as bytes.
public static class MitmFlowReader
{
    public sealed record MitmFlow(string Url, string Method, int Status, string? RequestDataB64, string ResponseBodyB64);

    // Yield every HTTP flow with a request and a response. Flows of other types (websocket, dns) and
    // request-only flows (no response captured) are skipped.
    public static IEnumerable<MitmFlow> Read(byte[] fileBytes)
    {
        var pos = 0;
        while (pos < fileBytes.Length)
        {
            object? value;
            int next;
            try { (value, next) = TnetString.Decode(fileBytes, pos); }
            catch (FormatException) { yield break; } // truncated tail: stop, keep what parsed

            pos = next;
            if (value is not Dictionary<string, object?> flow) continue;
            if (AsString(flow.GetValueOrDefault("type")) is { } t && t != "http") continue;

            if (flow.GetValueOrDefault("request") is not Dictionary<string, object?> req) continue;
            if (flow.GetValueOrDefault("response") is not Dictionary<string, object?> res) continue;

            var method = AsString(req.GetValueOrDefault("method")) ?? "";
            var url = BuildUrl(req);
            var status = AsInt(res.GetValueOrDefault("status_code"));
            if (url is null || status is null) continue;

            var requestData = ReadDataParam(AsBytes(req.GetValueOrDefault("content")));
            var responseBody = AsBytes(res.GetValueOrDefault("content"));
            if (responseBody is null) continue;

            yield return new MitmFlow(url, method, status.Value, requestData,
                Convert.ToBase64String(responseBody));
        }
    }

    private static string? BuildUrl(Dictionary<string, object?> req)
    {
        var scheme = AsString(req.GetValueOrDefault("scheme")) ?? "https";
        var host = AsString(req.GetValueOrDefault("host"));
        var path = AsString(req.GetValueOrDefault("path")) ?? "/";
        if (string.IsNullOrEmpty(host)) return null;
        var port = AsInt(req.GetValueOrDefault("port"));
        var authority = port is null or 80 or 443 ? host : $"{host}:{port}";
        return $"{scheme}://{authority}{path}";
    }

    // '+' restored before unescaping to match base64 padding (HAR path parity).
    private static string? ReadDataParam(byte[]? content)
    {
        if (content is null || content.Length == 0) return null;
        var body = Encoding.UTF8.GetString(content);
        foreach (var pair in body.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0 || pair[..eq] != "data") continue;
            return Uri.UnescapeDataString(pair[(eq + 1)..].Replace("+", "%2B"));
        }
        return null;
    }

    private static string? AsString(object? v) => v switch
    {
        byte[] b => Encoding.UTF8.GetString(b),
        string s => s,
        _ => null,
    };

    private static byte[]? AsBytes(object? v) => v switch
    {
        byte[] b => b,
        string s => Encoding.UTF8.GetBytes(s),
        _ => null,
    };

    private static int? AsInt(object? v) => v switch
    {
        long l => (int)l,
        int i => i,
        _ => null,
    };
}
