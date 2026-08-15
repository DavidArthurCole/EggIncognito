namespace EggIncognito.Services.Inspector;

public sealed record InspectorRef(string? EndpointPath, string? ObjectName) {
    public bool IsEmpty => string.IsNullOrEmpty(EndpointPath) && string.IsNullOrEmpty(ObjectName);
}

public static class InspectorRefParser {
    public const string Separator = "+";
    public const string EndpointPrefix = "ep:";
    public const string ObjectPrefix = "obj:";

    public static readonly InspectorRef Empty = new(null, null);

    public static InspectorRef Parse(string? hash) {
        if (string.IsNullOrWhiteSpace(hash)) return Empty;

        string body = hash.Trim();
        if (body.StartsWith('#')) body = body[1..];
        if (body.Length == 0) return Empty;

        if (!StartsWithKind(body)) {
            int slash = body.IndexOf('/');
            if (slash <= 0) return Empty;
            body = body[(slash + 1)..];
            if (!StartsWithKind(body)) return Empty;
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

        return path is null && name is null ? Empty : new InspectorRef(path, name);
    }

    public static string Format(InspectorRef value) {
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(value.EndpointPath)) parts.Add(EndpointPrefix + value.EndpointPath);
        if (!string.IsNullOrEmpty(value.ObjectName)) parts.Add(ObjectPrefix + value.ObjectName);
        return string.Join(Separator, parts);
    }

    private static bool StartsWithKind(string body) =>
        body.StartsWith(EndpointPrefix, StringComparison.Ordinal)
        || body.StartsWith(ObjectPrefix, StringComparison.Ordinal);
}
