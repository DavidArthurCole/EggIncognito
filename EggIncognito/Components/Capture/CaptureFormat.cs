using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EggIncognito.Components.Capture;

public static partial class CaptureFormat {
    public static readonly string[] JsonFormats = ["json-tree", "json", "yaml", "xml", "js"];
    public static readonly string[] ByteFormats = ["hex", "bin"];

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string> {
        ["json-tree"] = "JSON (tree)",
        ["json"] = "JSON (raw)",
        ["yaml"] = "YAML",
        ["xml"] = "XML",
        ["js"] = "JS object",
        ["hex"] = "Hex",
        ["bin"] = "Binary",
    };

    public static string Label(string fmt) => Labels.TryGetValue(fmt, out var l) ? l : fmt;

    public static bool IsByteFormat(string fmt) => Array.IndexOf(ByteFormats, fmt) >= 0;

    private static bool IsContainer(JsonNode? v) => v is JsonObject or JsonArray;



    public static string JsonToText(string? jsonStr, string fmt) {
        if (string.IsNullOrEmpty(jsonStr)) return "";
        JsonNode? value;
        try { value = JsonNode.Parse(jsonStr); } catch { return jsonStr; }
        return fmt switch {
            "json" => Reserialize(value),
            "yaml" => ToYaml(value, 0) is { Length: > 0 } y ? y : "{}",
            "xml" => PrettyXml(ToXml(value, "root")),
            "js" => ToJsLiteral(value, 0),
            _ => jsonStr,
        };
    }

    private static string Reserialize(JsonNode? value) =>
        value?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";

    public static string ToYaml(JsonNode? value, int indent) {
        var pad = new string(' ', indent * 2);
        switch (value) {
            case null:
                return "null";
            case JsonArray arr:
                if (arr.Count == 0) return "[]";
                return string.Join("\n", arr.Select(v => {
                    var child = ToYaml(v, indent + 1);
                    return IsContainer(v) ? $"{pad}-\n{child}" : $"{pad}- {child}";
                }));
            case JsonObject obj:
                if (obj.Count == 0) return "{}";
                return string.Join("\n", obj.Select(kv => {
                    var key = YamlKey(kv.Key);
                    var v = kv.Value;
                    var childCount = v is JsonArray a ? a.Count : v is JsonObject o ? o.Count : 0;
                    return IsContainer(v) && childCount > 0 ? $"{pad}{key}:\n{ToYaml(v, indent + 1)}" : $"{pad}{key}: {ToYaml(v, indent + 1)}";
                }));
            default:
                return ScalarYaml((JsonValue)value);
        }
    }

    private static string YamlKey(string k) =>
        BareYamlKeyRegex().IsMatch(k) ? k : JsonString(k);

    private static string ScalarYaml(JsonValue v) {
        if (v.TryGetValue(out string? s) && s is not null) {
            return s.Length == 0
                || System.Text.RegularExpressions.Regex.IsMatch(s, "[:#\\-?{}\\[\\],&*!|>'\"%@`]")
                || System.Text.RegularExpressions.Regex.IsMatch(s, "^\\s|\\s$")
                || System.Text.RegularExpressions.Regex.IsMatch(s, "^(true|false|null|~|\\d)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ? JsonString(s)
                : s;
        }
        return v.ToJsonString();
    }

    public static string ToXml(JsonNode? value, string root) => $"<{root}>{XmlBody(value)}</{root}>";

    private static string XmlBody(JsonNode? value) {
        return value switch {
            null => "",
            JsonArray arr => string.Concat(arr.Select(v => $"<item>{XmlBody(v)}</item>")),
            JsonObject obj => string.Concat(obj.Select(kv => {
                var tag = XmlTag(kv.Key);
                return $"<{tag}>{XmlBody(kv.Value)}</{tag}>";
            })),
            _ => XmlEscape(ScalarText((JsonValue)value)),
        };
    }

    private static string XmlTag(string k) {
        var t = System.Text.RegularExpressions.Regex.Replace(k, "[^A-Za-z0-9_.-]", "_");
        if (System.Text.RegularExpressions.Regex.IsMatch(t, "^[0-9.-]")) t = "_" + t;
        return t.Length == 0 ? "_" : t;
    }

    private static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string PrettyXml(string xml) {
        var sb = new StringBuilder();
        var depth = 0;
        var split = xml.Replace("><", ">\n<").Split('\n');
        foreach (var node in split) {
            if (System.Text.RegularExpressions.Regex.IsMatch(node, "^</\\w")) depth--;
            sb.Append(new string(' ', Math.Max(0, depth) * 2)).Append(node).Append('\n');
            if (System.Text.RegularExpressions.Regex.IsMatch(node, "^<\\w[^>]*[^/]>$")
                && !System.Text.RegularExpressions.Regex.IsMatch(node, "^<.*</.*>$")) {
                depth++;
            }
        }
        return sb.ToString().Trim();
    }

    public static string ToJsLiteral(JsonNode? value, int indent) {
        var pad = new string(' ', indent * 2);
        var padIn = new string(' ', (indent + 1) * 2);
        switch (value) {
            case null:
                return "null";
            case JsonArray arr:
                if (arr.Count == 0) return "[]";
                var items = arr.Select(v => padIn + ToJsLiteral(v, indent + 1));
                return $"[\n{string.Join(",\n", items)}\n{pad}]";
            case JsonObject obj:
                if (obj.Count == 0) return "{}";
                var rows = obj.Select(kv => $"{padIn}{JsKey(kv.Key)}: {ToJsLiteral(kv.Value, indent + 1)}");
                return $"{{\n{string.Join(",\n", rows)}\n{pad}}}";
            default:
                var jv = (JsonValue)value;
                return jv.TryGetValue(out string? s) && s is not null ? JsonString(s) : jv.ToJsonString();
        }
    }

    private static string JsKey(string k) =>
        System.Text.RegularExpressions.Regex.IsMatch(k, "^[A-Za-z_$][A-Za-z0-9_$]*$") ? k : JsonString(k);

    private static string ScalarText(JsonValue v) =>
        v.TryGetValue(out string? s) && s is not null ? s : v.ToJsonString();

    private static string JsonString(string s) => JsonSerializer.Serialize(s);


    public static byte[] BytesFromBase64(string? b64) {
        if (string.IsNullOrEmpty(b64)) return [];
        var s = b64.Trim().Replace(' ', '+');
        var pad = s.Length % 4;
        if (pad != 0) s += new string('=', 4 - pad);
        try { return Convert.FromBase64String(s); } catch { return []; }
    }


    public static string ToHexDump(byte[] bytes) {
        if (bytes.Length == 0) return "(empty)";
        var lines = new List<string>();
        for (var i = 0; i < bytes.Length; i += 16) {
            var slice = bytes.Skip(i).Take(16).ToArray();
            var off = i.ToString("x8");
            var hex = string.Join(" ", slice.Select(b => b.ToString("x2"))).PadRight(16 * 3 - 1, ' ');
            var ascii = string.Concat(slice.Select(b => b is >= 32 and < 127 ? (char)b : '.'));
            lines.Add($"{off}  {hex}  |{ascii}|");
        }
        return string.Join("\n", lines);
    }


    public static string ToBinDump(byte[] bytes) {
        if (bytes.Length == 0) return "(empty)";
        var lines = new List<string>();
        for (var i = 0; i < bytes.Length; i += 8) {
            var slice = bytes.Skip(i).Take(8).ToArray();
            var off = i.ToString("x8");
            var bits = string.Join(" ", slice.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
            lines.Add($"{off}  {bits}");
        }
        return string.Join("\n", lines);
    }

    public static string BytesToText(string? b64, string fmt) {
        var bytes = BytesFromBase64(b64);
        return fmt == "bin" ? ToBinDump(bytes) : ToHexDump(bytes);
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial System.Text.RegularExpressions.Regex BareYamlKeyRegex();
}
