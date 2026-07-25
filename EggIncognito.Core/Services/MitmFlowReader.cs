using System.Text;

namespace EggIncognito.Services;

public static class MitmFlowReader {
    public static IEnumerable<MitmFlow> Read(byte[] fileBytes) {
        int pos = 0;
        while (pos < fileBytes.Length) {
            object? value;
            int next;
            try {
                (value, next) = TnetString.Decode(fileBytes, pos);
            } catch (FormatException) {
                yield break;
            }

            pos = next;
            if (value is not Dictionary<string, object?> flow) continue;
            if (AsString(flow.GetValueOrDefault("type")) is { } t && t != "http") continue;

            if (flow.GetValueOrDefault("request") is not Dictionary<string, object?> req) continue;
            if (flow.GetValueOrDefault("response") is not Dictionary<string, object?> res) continue;

            string method = AsString(req.GetValueOrDefault("method")) ?? "";
            string? url = BuildUrl(req);
            int? status = AsInt(res.GetValueOrDefault("status_code"));
            if (url is null || status is null) continue;

            string? requestData = ReadDataParam(AsBytes(req.GetValueOrDefault("content")));
            byte[]? responseBody = AsBytes(res.GetValueOrDefault("content"));
            if (responseBody is null) continue;

            yield return new MitmFlow(url, method, status.Value, requestData,
                Convert.ToBase64String(responseBody));
        }
    }

    private static string? BuildUrl(Dictionary<string, object?> req) {
        string scheme = AsString(req.GetValueOrDefault("scheme")) ?? "https";
        string? host = AsString(req.GetValueOrDefault("host"));
        string path = AsString(req.GetValueOrDefault("path")) ?? "/";
        if (string.IsNullOrEmpty(host)) return null;
        int? port = AsInt(req.GetValueOrDefault("port"));
        string authority = port is null or 80 or 443 ? host : $"{host}:{port}";
        return $"{scheme}://{authority}{path}";
    }


    private static string? ReadDataParam(byte[]? content) {
        if (content is null || content.Length == 0) return null;
        string body = Encoding.UTF8.GetString(content);
        foreach (string pair in body.Split('&')) {
            int eq = pair.IndexOf('=');
            if (eq < 0 || pair[..eq] != "data") continue;
            return Uri.UnescapeDataString(pair[(eq + 1)..].Replace("+", "%2B"));
        }

        return null;
    }

    private static string? AsString(object? v) => v switch {
        byte[] b => Encoding.UTF8.GetString(b),
        string s => s,
        _ => null
    };

    private static byte[]? AsBytes(object? v) => v switch {
        byte[] b => b,
        string s => Encoding.UTF8.GetBytes(s),
        _ => null
    };

    private static int? AsInt(object? v) => v switch {
        long l => (int)l,
        int i => i,
        _ => null
    };

    public sealed record MitmFlow(
        string Url,
        string Method,
        int Status,
        string? RequestDataB64,
        string ResponseBodyB64);
}
