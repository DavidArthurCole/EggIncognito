namespace EggIncognito.Components.Inspector;
public sealed record InspectorHistoryEntry(
    string Id,
    string Path,
    string Summary,
    Dictionary<string, string> Env,
    string FieldsJson,
    string? PathParam,
    string Target,
    long Order);
