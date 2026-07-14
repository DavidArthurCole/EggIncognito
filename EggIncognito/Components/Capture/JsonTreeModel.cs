using System.Text.Json;
using System.Text.Json.Nodes;

namespace EggIncognito.Components.Capture;

// Parsed model behind JsonTree.razor: the whole value is parsed once into TreeNode records and the
// component renders only expanded branches. Expansion + search dim/match flags are mutable per node.
public sealed class TreeNode
{
    public const int DefaultDepth = 1;
    public const int SmallChildLimit = 5;
    public const int ValueClamp = 200;

    // "object" | "array" | "string" | "number" | "boolean" | "null"
    public string Kind { get; init; } = "null";
    public string? KeyText { get; init; } // object key or array index label, null for root
    public string? KeyName { get; init; } // JSON field name governing sensitivity (array items inherit)
    public int Depth { get; init; }

    // leaf value (rendered text, already quoted for strings)
    public string LeafText { get; init; } = "";

    // lowercased key/leaf cached once at parse so search never re-lowers per keystroke
    public string? KeyTextLower { get; private set; }
    public string LeafTextLower { get; private set; } = "";

    public bool IsContainer => Kind is "object" or "array";

    public List<TreeNode> Children { get; } = [];

    // runtime ui state
    public bool Expanded { get; set; }
    public bool Dim { get; set; }
    public bool SelfMatch { get; set; }

    public int ChildCount => Children.Count;

    public string Summary => Kind == "array"
        ? $"[...] {ChildCount} {(ChildCount == 1 ? "item" : "items")}"
        : $"{{...}} {ChildCount} {(ChildCount == 1 ? "key" : "keys")}";

    // Default expansion at this depth: within the default depth or small.
    public bool ShouldDefaultExpand() => Depth <= DefaultDepth || ChildCount < SmallChildLimit;

    // Build a tree from a JSON string. Returns null on parse failure or empty input.
    public static TreeNode? Parse(string? jsonStr)
    {
        if (string.IsNullOrEmpty(jsonStr)) return null;
        JsonNode? root;
        try { root = JsonNode.Parse(jsonStr); }
        catch { return null; }
        return Build(null, root, 0, null);
    }

    static TreeNode Build(string? keyText, JsonNode? value, int depth, string? keyName)
    {
        var kind = ValueKind(value);
        var node = new TreeNode
        {
            Kind = kind,
            KeyText = keyText,
            KeyName = keyName,
            Depth = depth,
            LeafText = kind is "object" or "array" ? "" : LeafValue(value, kind),
        };
        node.KeyTextLower = keyText?.ToLowerInvariant();
        node.LeafTextLower = node.LeafText.ToLowerInvariant();

        if (value is JsonArray arr)
        {
            // Array items have no key of their own; inherit the array's field name.
            for (var i = 0; i < arr.Count; i++)
                node.Children.Add(Build(i.ToString(), arr[i], depth + 1, keyName));
        }
        else if (value is JsonObject obj)
        {
            foreach (var kv in obj)
                node.Children.Add(Build(kv.Key, kv.Value, depth + 1, kv.Key));
        }

        if (node.IsContainer) node.Expanded = node.ShouldDefaultExpand();
        return node;
    }

    static string ValueKind(JsonNode? v)
    {
        if (v is null) return "null";
        if (v is JsonArray) return "array";
        if (v is JsonObject) return "object";
        var jv = (JsonValue)v;
        if (jv.TryGetValue(out bool _)) return "boolean";
        if (jv.TryGetValue(out string? _)) return "string";
        return "number";
    }

    // Leaf rendering matching renderLeafValue: strings quoted, others stringified.
    static string LeafValue(JsonNode? v, string kind)
    {
        if (v is null) return "null";
        var jv = (JsonValue)v;
        if (kind == "string" && jv.TryGetValue(out string? s) && s is not null)
            return "\"" + s + "\"";
        if (kind == "boolean" && jv.TryGetValue(out bool b)) return b ? "true" : "false";
        return jv.ToJsonString(); // numbers render canonically
    }
}
