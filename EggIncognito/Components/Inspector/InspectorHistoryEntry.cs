namespace EggIncognito.Components.Inspector;

// One saved Inspector request, persisted client-side (browser localStorage via inspectorStore.js). Holds
// enough to restore the builder: the endpoint path, the env (BasicRequestInfo) overrides, the body fields
// JSON, the path param, and the chosen target. Summary is a short human label for the history list.
public sealed record InspectorHistoryEntry(
    string Id,
    string Path,
    string Summary,
    Dictionary<string, string> Env,
    string FieldsJson,
    string? PathParam,
    string Target,
    long Order);
