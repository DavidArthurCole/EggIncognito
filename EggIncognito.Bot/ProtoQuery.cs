using EggIncognito.Services;

namespace EggIncognito.Bot;

// Pure helpers for the /proto command: page a name list, filter for autocomplete, render a message
// type's fields. No Discord / reflection state here - the router passes in the names + schema.
public static class ProtoQuery
{
    public const int PerPage = 25;

    // 1-based paging. Returns the slice, the clamped page, and the total page count.
    public static (IReadOnlyList<string> Slice, int Page, int Pages) Page(
        IReadOnlyList<string> names, int requestedPage, int perPage = PerPage)
    {
        var pages = Math.Max(1, (names.Count + perPage - 1) / perPage);
        var page = Math.Clamp(requestedPage, 1, pages);
        var slice = names.Skip((page - 1) * perPage).Take(perPage).ToList();
        return (slice, page, pages);
    }

    // Up to 25 case-insensitive "contains" matches (Discord caps autocomplete at 25).
    public static IReadOnlyList<string> Autocomplete(IReadOnlyList<string> names, string query)
    {
        if (string.IsNullOrEmpty(query)) return names.Take(25).ToList();
        return names
            .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(25).ToList();
    }

    // Discord caps embed descriptions at 4096 chars; the router wraps type dumps in a code fence.
    // 4000 leaves headroom for the fence + newlines so EmbedBuilder.Build() never throws.
    public const int MaxDescription = 4000;

    // Clamps text to max chars, replacing the tail with a truncation marker when over budget.
    public static string Truncate(string text, int max = MaxDescription)
    {
        const string marker = "\n... (truncated)";
        if (text.Length <= max) return text;
        return text[..Math.Max(0, max - marker.Length)] + marker;
    }

    // One line per field: "#N name: type[<MessageType>][ repeated][ = enum{A,B}]".
    public static string TypeLines(SchemaMessage msg)
    {
        if (msg.Fields.Count == 0) return "(no fields)";
        var lines = msg.Fields.Select(f =>
        {
            var rep = f.Repeated ? " repeated" : "";
            var en = f.EnumValues is { Count: > 0 }
                ? " = enum{" + string.Join(",", f.EnumValues.Select(v => v.Name)) + "}"
                : "";
            var mt = f.Type == "message" && f.MessageType is not null ? $"<{f.MessageType}>" : "";
            return $"#{f.Number} {f.Name}: {f.Type}{mt}{rep}{en}";
        });
        return string.Join("\n", lines);
    }
}
