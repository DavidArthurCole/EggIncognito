using System.Text.Json;
using System.Text.Json.Nodes;

namespace EggIncognito.Components.Capture;

public sealed class TreeNode
{
    public const int DefaultDepth = 1;
    public const int SmallChildLimit = 5;
    public const int ValueClamp = 200;

   
    public string Kind { get; init; } = "null";
    public string? KeyText { get; init; }
    public string? KeyName { get; init; }
    public int Depth { get; init; }

   
    public string LeafText { get; init; } = "";

   
    public string? KeyTextLower { get; private set; }
    public string LeafTextLower { get; private set; } = "";

    public bool IsContainer => Kind is "object" or "array";

    public List<TreeNode> Children { get; } = [];

   
    public bool Expanded { get; set; }
    public bool Dim { get; set; }
    public bool SelfMatch { get; set; }

    public int ChildCount => Children.Count;

    public string Summary => Kind == "array"
        ? $"[...] {ChildCount} {(ChildCount == 1 ? "item" : "items")}"
        : $"{{...}} {ChildCount} {(ChildCount == 1 ? "key" : "keys")}";

   
    public bool ShouldDefaultExpand() => Depth <= DefaultDepth || ChildCount < SmallChildLimit;

   
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

   
    static string LeafValue(JsonNode? v, string kind)
    {
        if (v is null) return "null";
        var jv = (JsonValue)v;
        if (kind == "string" && jv.TryGetValue(out string? s) && s is not null)
            return "\"" + s + "\"";
        if (kind == "boolean" && jv.TryGetValue(out bool b)) return b ? "true" : "false";
        return jv.ToJsonString();
    }
}
