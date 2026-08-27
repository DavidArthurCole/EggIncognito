using System.Globalization;
using System.Text.Json.Nodes;

namespace EggIncognito.Components.Shared.Code;

public sealed class CodeTreeNode {
    public const int DefaultDepth = 1;
    public const int SmallChildLimit = 5;
    public const int LargeChildLimit = 100;
    public const int ValueClamp = 200;

    public string Kind { get; init; } = "null";
    public string? KeyText { get; init; }
    public string? KeyName { get; init; }
    public int Depth { get; init; }

    public string LeafText { get; init; } = "";

    public string? KeyTextLower { get; private set; }
    public string LeafTextLower { get; private set; } = "";

    public bool IsContainer => Kind is "object" or "array";

    public List<CodeTreeNode> Children { get; } = [];

    public bool Expanded { get; set; }
    public bool Dim { get; set; }
    public bool SelfMatch { get; set; }

    public int ChildCount => Children.Count;

    public int DescendantCount {
        get {
            int n = Children.Count;
            foreach (var c in Children) n += c.DescendantCount;
            return n;
        }
    }

    public string Summary => Kind == "array"
        ? $"[...] {ChildCount} {(ChildCount == 1 ? "item" : "items")}"
        : $"{{...}} {ChildCount} {(ChildCount == 1 ? "key" : "keys")}";

    public string TokenClass => Kind switch {
        "string" => "tok-string",
        "number" => "tok-number",
        "boolean" => "tok-bool",
        "null" => "tok-null",
        _ => "tok-plain"
    };

    public bool ShouldDefaultExpand() => (Depth <= DefaultDepth || ChildCount < SmallChildLimit) && ChildCount <= LargeChildLimit;

    public static CodeTreeNode? Parse(string? jsonStr) {
        if (string.IsNullOrEmpty(jsonStr)) return null;
        JsonNode? root;
        try {
            root = JsonNode.Parse(jsonStr);
        } catch {
            return null;
        }

        return Build(null, root, 0, null);
    }

    private static CodeTreeNode Build(string? keyText, JsonNode? value, int depth, string? keyName) {
        string kind = ValueKind(value);
        var node = new CodeTreeNode {
            Kind = kind,
            KeyText = keyText,
            KeyName = keyName,
            Depth = depth,
            LeafText = kind is "object" or "array" ? "" : LeafValue(value, kind),
            KeyTextLower = keyText?.ToLowerInvariant()
        };
        node.LeafTextLower = node.LeafText.ToLowerInvariant();

        if (value is JsonArray arr) {
            for (int i = 0; i < arr.Count; i++)
                node.Children.Add(Build(i.ToString(CultureInfo.InvariantCulture), arr[i], depth + 1, keyName));
        } else if (value is JsonObject obj) {
            foreach (var kv in obj)
                node.Children.Add(Build(kv.Key, kv.Value, depth + 1, kv.Key));
        }

        if (node.IsContainer) node.Expanded = node.ShouldDefaultExpand();
        return node;
    }

    private static string ValueKind(JsonNode? v) {
        if (v is null) return "null";
        if (v is JsonArray) return "array";
        if (v is JsonObject) return "object";
        var jv = (JsonValue)v;
        return jv.TryGetValue(out bool _) ? "boolean" : jv.TryGetValue(out string? _) ? "string" : "number";
    }

    private static string LeafValue(JsonNode? v, string kind) {
        if (v is null) return "null";
        var jv = (JsonValue)v;
        return kind == "string" && jv.TryGetValue(out string? s) && s is not null
            ? "\"" + s + "\""
            : kind == "boolean" && jv.TryGetValue(out bool b)
                ? b ? "true" : "false"
                : jv.ToJsonString();
    }
}
