using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Services;

namespace EggIncognito.Components.Inspector;

// The editable request-body tree, built from proto schema (IProtoReflection). One node per proto field;
// message fields expand to child nodes by resolving their sub-schema, repeated fields hold a list of
// scalar item values, scalars/enums/bools hold a single string value. Collect() walks the tree into the
// Google.Protobuf JSON object the /build call expects (the same shape the old app.js collectFields
// produced). Mirrors the recursion in wwwroot/inspector/app.js fieldRow.
public sealed class FieldNode
{
    public required SchemaField Field { get; init; }
    // jsonName chain from the root, used only for the env-lock match (rinfo.<key>).
    public required string PathKey { get; init; }

    // Scalar/enum/bool single value (empty => unset, omitted from JSON).
    public string Value { get; set; } = "";
    // Repeated item values; each is coerced like a scalar.
    public List<string> Items { get; set; } = [];
    // Child nodes for a message field.
    public List<FieldNode> Children { get; set; } = [];

    // Env-lock: when the Environment panel sets this rinfo.<key>, the input is mirrored + disabled so
    // the two cannot desync. An empty env value releases the lock.
    public bool Locked { get; set; }

    public bool IsMessage => Field.Type == "message";
    public bool IsEnum => Field.Type == "enum";
    public bool IsBool => Field.Type == "bool";
    public bool IsRepeated => Field.Repeated && !IsMessage;

    public void AddItem() => Items.Add("");
    public void RemoveItem(int i) { if (i >= 0 && i < Items.Count) Items.RemoveAt(i); }
}

public static class FieldTreeBuilder
{
    // Build the top-level node list for a request type. schemaOf resolves a type name to its schema
    // (cached by the caller). Recurses into message fields.
    public static List<FieldNode> Build(SchemaMessage schema, Func<string, SchemaMessage?> schemaOf) =>
        schema.Fields.Select(f => BuildNode(f, [], schemaOf)).ToList();

    private static FieldNode BuildNode(SchemaField f, IReadOnlyList<string> path,
        Func<string, SchemaMessage?> schemaOf)
    {
        var chain = path.Append(f.JsonName).ToList();
        var node = new FieldNode { Field = f, PathKey = string.Join(".", chain) };
        if (f.Type == "message" && f.MessageType is not null)
        {
            var sub = schemaOf(f.MessageType);
            if (sub is not null)
                node.Children = sub.Fields.Select(cf => BuildNode(cf, chain, schemaOf)).ToList();
        }
        return node;
    }

    // proto numeric type groups, matching app.js coerce().
    private static readonly HashSet<string> Int32 =
        ["int32", "uint32", "sint32", "fixed32", "sfixed32"];
    private static readonly HashSet<string> Int64 =
        ["int64", "uint64", "sint64", "fixed64", "sfixed64"];
    private static readonly HashSet<string> Floats = ["double", "float"];

    // Coerce a raw string to the JSON node the proto JSON parser expects. Returns null to omit the
    // field (empty value). 64-bit ints stay strings in protojson (can exceed JS/number precision).
    private static JsonNode? Coerce(string raw, string ptype)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (ptype == "bool") return JsonValue.Create(raw == "true");
        if (Int32.Contains(ptype))
            return int.TryParse(raw, out var i) ? JsonValue.Create(i) : JsonValue.Create(raw);
        if (Int64.Contains(ptype)) return JsonValue.Create(raw); // string in protojson
        if (Floats.Contains(ptype))
            return double.TryParse(raw, out var d) ? JsonValue.Create(d) : JsonValue.Create(raw);
        return JsonValue.Create(raw); // string, enum (name), bytes (b64)
    }

    // Walk the tree into the protojson request object. Skips unset fields.
    public static JsonObject Collect(IReadOnlyList<FieldNode> nodes)
    {
        var obj = new JsonObject();
        foreach (var n in nodes)
        {
            if (n.IsMessage)
            {
                var child = Collect(n.Children);
                if (child.Count > 0) obj[n.Field.JsonName] = child;
            }
            else if (n.Field.Repeated)
            {
                var arr = new JsonArray();
                foreach (var item in n.Items)
                {
                    var v = Coerce(item, n.Field.Type);
                    if (v is not null) arr.Add(v);
                }
                if (arr.Count > 0) obj[n.Field.JsonName] = arr;
            }
            else
            {
                var v = Coerce(n.Value, n.Field.Type);
                if (v is not null) obj[n.Field.JsonName] = v;
            }
        }
        return obj;
    }

    // Apply the env panel's BasicRequestInfo overrides onto the matching rinfo.<key> tree input, locking
    // each set one. An empty env value releases the lock. Env keys with no matching input are skipped.
    // Mirrors app.js applyEnvLock.
    public static void ApplyEnvLock(IReadOnlyList<FieldNode> nodes, IReadOnlyDictionary<string, string> env)
    {
        var rinfo = nodes.FirstOrDefault(n => n.Field.JsonName == "rinfo" && n.IsMessage);
        if (rinfo is null) return;
        foreach (var child in rinfo.Children)
        {
            if (env.TryGetValue(child.Field.JsonName, out var v) && !string.IsNullOrEmpty(v))
            {
                child.Value = v;
                child.Locked = true;
            }
            else
            {
                child.Locked = false;
            }
        }
    }
}

// One Environment-panel row (a BasicRequestInfo override). Value typed as string; type recorded so
// Collect coerces number/bool keys when building the env object sent to /build.
public sealed class EnvRow
{
    public required string Key { get; init; }
    public required string ValueType { get; init; } // "number" | "boolean" | "string"
    public string Value { get; set; } = "";
}

public static class EnvCollector
{
    // Collect the env rows into the JSON object /build's MergeEnv expects.
    public static Dictionary<string, object?> Collect(IEnumerable<EnvRow> rows)
    {
        var env = new Dictionary<string, object?>();
        foreach (var r in rows)
        {
            object? v = r.ValueType switch
            {
                "number" => int.TryParse(r.Value, out var i) ? i : (object?)r.Value,
                "boolean" => r.Value == "true",
                _ => r.Value,
            };
            env[r.Key] = v;
        }
        return env;
    }

    public static Dictionary<string, string> AsStrings(IEnumerable<EnvRow> rows) =>
        rows.ToDictionary(r => r.Key, r => r.Value);
}
