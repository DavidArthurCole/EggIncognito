using EggIncognito.Services;

namespace EggIncognito.Bot;

public static class ProtoQuery {
    public const int PerPage = 25;


    public static (IReadOnlyList<string> Slice, int Page, int Pages) Page(
        IReadOnlyList<string> names, int requestedPage, int perPage = PerPage) {
        var pages = Math.Max(1, (names.Count + perPage - 1) / perPage);
        var page = Math.Clamp(requestedPage, 1, pages);
        var slice = names.Skip((page - 1) * perPage).Take(perPage).ToList();
        return (slice, page, pages);
    }


    public static IReadOnlyList<string> Autocomplete(IReadOnlyList<string> names, string query) {
        return string.IsNullOrEmpty(query)
            ? names.Take(25).ToList()
            : names
            .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(25).ToList();
    }



    public const int MaxDescription = 4000;


    public static string Truncate(string text, int max = MaxDescription) {
        const string marker = "\n... (truncated)";
        return text.Length <= max ? text : text[..Math.Max(0, max - marker.Length)] + marker;
    }


    public static string TypeLines(SchemaMessage msg) {
        if (msg.Fields.Count == 0) return "(no fields)";
        var lines = msg.Fields.Select(f => {
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
