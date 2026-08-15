namespace EggIncognito.Services.Inspector;

public sealed record InspectorRef(string? EndpointPath, string? ObjectName, InspectorReaderMode Mode) {
    public bool IsEmpty => string.IsNullOrEmpty(EndpointPath) && string.IsNullOrEmpty(ObjectName);
}

public static class InspectorRefParser {
    public const string Separator = "+";
    public const string EndpointPrefix = "ep:";
    public const string ObjectPrefix = "obj:";
    public const string ResultMode = "result";
    public const string ReferenceMode = "reference";

    public static readonly string[] Modes = [ResultMode, ReferenceMode];

    public static readonly InspectorRef Empty = new(null, null, InspectorReaderMode.Result);

    public static InspectorRef Parse(string? hash) {
        if (string.IsNullOrWhiteSpace(hash)) return Empty;

        string body = hash.Trim();
        if (body.StartsWith('#')) body = body[1..];
        if (body.Length == 0) return Empty;

        var mode = InspectorReaderMode.Result;
        int slash = body.IndexOf('/');
        if (slash > 0 && !StartsWithKind(body)) {
            mode = ModeOf(body[..slash]);
            body = body[(slash + 1)..];
        }

        string? path = null;
        string? name = null;
        int taken = 0;
        foreach (string part in body.Split(Separator, StringSplitOptions.RemoveEmptyEntries)) {
            if (taken >= 2) break;
            taken++;
            if (path is null && part.StartsWith(EndpointPrefix, StringComparison.Ordinal)) {
                string value = part[EndpointPrefix.Length..].Trim();
                if (value.Length > 0) path = value;
            } else if (name is null && part.StartsWith(ObjectPrefix, StringComparison.Ordinal)) {
                string value = part[ObjectPrefix.Length..].Trim();
                if (value.Length > 0) name = value;
            }
        }

        if (path is null && name is null) return Empty;
        if (name is not null) mode = InspectorReaderMode.Reference;
        return new InspectorRef(path, name, mode);
    }

    public static string Format(InspectorRef value) {
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(value.EndpointPath)) parts.Add(EndpointPrefix + value.EndpointPath);
        if (!string.IsNullOrEmpty(value.ObjectName)) parts.Add(ObjectPrefix + value.ObjectName);
        if (parts.Count == 0) return "";

        string body = string.Join(Separator, parts);
        bool writeMode = value.Mode == InspectorReaderMode.Reference
                         && string.IsNullOrEmpty(value.ObjectName);
        return writeMode ? ReferenceMode + "/" + body : body;
    }

    private static bool StartsWithKind(string body) =>
        body.StartsWith(EndpointPrefix, StringComparison.Ordinal)
        || body.StartsWith(ObjectPrefix, StringComparison.Ordinal);

    private static InspectorReaderMode ModeOf(string candidate) =>
        candidate.Equals(ReferenceMode, StringComparison.OrdinalIgnoreCase)
            ? InspectorReaderMode.Reference
            : InspectorReaderMode.Result;
}
