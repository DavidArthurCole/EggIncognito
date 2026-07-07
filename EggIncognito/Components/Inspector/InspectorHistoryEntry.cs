namespace EggIncognito.Components.Inspector;

// Persisted client-side (browser localStorage via inspectorStore.js), not server-side.
public sealed record InspectorHistoryEntry(
    string Id,
    string Path,
    string Summary,
    Dictionary<string, string> Env,
    string FieldsJson,
    string? PathParam,
    string Target,
    long Order);
