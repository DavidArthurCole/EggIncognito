// Walks the compiled Ei.* message descriptors via Google.Protobuf reflection to produce the field tree
// the Inspector UI renders, and resolves message types and parsers by name. Always in sync with the
// real proto - no text parsing.

using System.Collections.Concurrent;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.Services;

public sealed record SchemaEnumValue(string Name, int Number);

public sealed record SchemaField(
    string Name,
    string JsonName,
    int Number,
    string Type, // proto field type, e.g. "string", "uint32", "message", "enum"
    bool Repeated,
    bool Required,
    string? MessageType, // short Ei type name, set when Type == "message"
    IReadOnlyList<SchemaEnumValue>? EnumValues);

public sealed record SchemaMessage(string Name, IReadOnlyList<SchemaField> Fields);

public interface IProtoReflection
{
    MessageDescriptor? FindMessage(string typeName);
    MessageParser? FindParser(string typeName);
    SchemaMessage? Schema(string typeName);
    // Every concrete Ei.* message type's short name, sorted. Powers the Inspector "Objects" list and
    // the Documentation feature, one doc/tag subject per message type.
    IReadOnlyList<string> AllMessageTypeNames();
}

public sealed class ProtoReflection : IProtoReflection
{
    private static readonly Assembly EiAssembly = typeof(Ei.AuthenticatedMessage).Assembly;

    // typeName may be "ContractsInfoRequest" or "Ei.ContractsInfoRequest".
    private static string Short(string typeName) =>
        typeName.StartsWith("Ei.", StringComparison.Ordinal) ? typeName[3..] : typeName;

    // Positive entries only: misses from unknown user-input names are not cached.
    private static readonly ConcurrentDictionary<string, (MessageDescriptor Descriptor, MessageParser Parser)> Cache =
        new(StringComparer.Ordinal);

    private static (MessageDescriptor Descriptor, MessageParser Parser)? Resolve(string typeName)
    {
        var key = Short(typeName);
        if (Cache.TryGetValue(key, out var hit)) return hit;

        var clr = EiAssembly.GetType("Ei." + key);
        var descriptor = clr?.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as MessageDescriptor;
        var parser = clr?.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as MessageParser;
        if (descriptor is null || parser is null) return null;

        Cache.TryAdd(key, (descriptor, parser));
        return (descriptor, parser);
    }

    // Cached: every top-level concrete IMessage type in the Ei assembly, by short name, sorted. Only top-level
    // types are included so every listed name resolves through ClrType("Ei." + name).
    private static readonly Lazy<IReadOnlyList<string>> AllNames = new(() =>
        EiAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, DeclaringType: null }
                && t.Namespace == "Ei"
                && typeof(IMessage).IsAssignableFrom(t)
                && t.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static) is not null)
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList());

    public IReadOnlyList<string> AllMessageTypeNames() => AllNames.Value;

    public MessageDescriptor? FindMessage(string typeName) => Resolve(typeName)?.Descriptor;

    public MessageParser? FindParser(string typeName) => Resolve(typeName)?.Parser;

    public SchemaMessage? Schema(string typeName)
    {
        var desc = FindMessage(typeName);
        if (desc is null) return null;

        var fields = desc.Fields.InFieldNumberOrder().Select(ToSchemaField).ToList();
        return new SchemaMessage(desc.Name, fields);
    }

    private static SchemaField ToSchemaField(FieldDescriptor f)
    {
        string type = f.FieldType.ToString().ToLowerInvariant();
        string? messageType = null;
        IReadOnlyList<SchemaEnumValue>? enumValues = null;

        if (f.FieldType == FieldType.Message || f.FieldType == FieldType.Group)
        {
            type = "message";
            messageType = f.MessageType.Name;
        }
        else if (f.FieldType == FieldType.Enum)
        {
            type = "enum";
            enumValues = f.EnumType.Values
                .Select(v => new SchemaEnumValue(v.Name, v.Number))
                .ToList();
        }

        return new SchemaField(
            Name: f.Name,
            JsonName: f.JsonName,
            Number: f.FieldNumber,
            Type: type,
            Repeated: f.IsRepeated,
            Required: f.IsRequired,
            MessageType: messageType,
            EnumValues: enumValues);
    }
}
