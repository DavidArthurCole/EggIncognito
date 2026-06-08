// EggIncognito.Core/Services/ProtoReflection.cs
//
// Walks the compiled Ei.* message descriptors (Google.Protobuf reflection) to produce
// the field tree the Inspector UI renders, and resolves message types / parsers by name.
// Always in sync with the real proto - no text parsing.

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
    string? MessageType, // set when Type == "message" (short Ei type name)
    IReadOnlyList<SchemaEnumValue>? EnumValues);

public sealed record SchemaMessage(string Name, IReadOnlyList<SchemaField> Fields);

public interface IProtoReflection
{
    MessageDescriptor? FindMessage(string typeName);
    MessageParser? FindParser(string typeName);
    SchemaMessage? Schema(string typeName);
}

public sealed class ProtoReflection : IProtoReflection
{
    private static readonly Assembly EiAssembly = typeof(Ei.AuthenticatedMessage).Assembly;

    // typeName may be "ContractsInfoRequest" or "Ei.ContractsInfoRequest".
    private static string Short(string typeName) =>
        typeName.StartsWith("Ei.", StringComparison.Ordinal) ? typeName[3..] : typeName;

    private Type? ClrType(string typeName) =>
        EiAssembly.GetType("Ei." + Short(typeName));

    public MessageDescriptor? FindMessage(string typeName)
    {
        var clr = ClrType(typeName);
        var prop = clr?.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static);
        return prop?.GetValue(null) as MessageDescriptor;
    }

    public MessageParser? FindParser(string typeName)
    {
        var clr = ClrType(typeName);
        var prop = clr?.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static);
        return prop?.GetValue(null) as MessageParser;
    }

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
