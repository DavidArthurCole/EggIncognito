// Layer A of the docs hub: the typed registry of documentable subjects. It enumerates the four
// subject kinds (proto messages, mock endpoints, config options, UI controls) into a tree of
// DocSubject and exposes O(1) lookup by (kind, key). Singleton; the tree is built once in the ctor.
// The message + endpoint subtrees are derived from the live proto reflection + route catalog, so they
// stay in sync with the real surface. Config + control subjects are curated static lists.

namespace EggIncognito.Services;

// A node in the docs tree. Children is always a list (use [] for none, never null).
public sealed record DocSubject(
    string Kind,
    string Key,
    string Title,
    string? Summary,
    IReadOnlyList<DocSubject> Children);

public interface IDocRegistry
{
    IReadOnlyList<DocSubject> Roots(); // the 4 group nodes: Messages, Endpoints, Config, Controls
    DocSubject? Find(string kind, string key);
}

public sealed class DocRegistry : IDocRegistry
{
    private readonly IReadOnlyList<DocSubject> _roots;
    private readonly Dictionary<string, DocSubject> _byKey; // "{kind}:{key}" -> subject

    public DocRegistry(IProtoReflection proto, IRouteCatalog routes)
    {
        var messages = BuildMessages(proto);
        var endpoints = BuildEndpoints(routes, proto);
        var config = BuildConfig();
        var controls = BuildControls();

        _roots =
        [
            new DocSubject("group", "messages", "Messages", "Egg, Inc. proto message types", messages),
            new DocSubject("group", "endpoints", "Endpoints", "Mock API routes", endpoints),
            new DocSubject("group", "config", "Config", "Configuration options", config),
            new DocSubject("group", "controls", "Controls", "UI controls", controls),
        ];

        // Flat index for Find. Group nodes and message field children are intentionally indexed too
        // so they are addressable; later inserts for the same key win (no collisions expected).
        _byKey = new Dictionary<string, DocSubject>(StringComparer.Ordinal);
        foreach (var root in _roots)
        {
            Index(root);
            foreach (var child in root.Children) Index(child);
        }
    }

    public IReadOnlyList<DocSubject> Roots() => _roots;

    public DocSubject? Find(string kind, string key) =>
        _byKey.TryGetValue($"{kind}:{key}", out var s) ? s : null;

    private void Index(DocSubject s) => _byKey[$"{s.Kind}:{s.Key}"] = s;

    // One subject per top-level Ei.* message type. Children are the message fields (display-only).
    private static IReadOnlyList<DocSubject> BuildMessages(IProtoReflection proto)
    {
        var list = new List<DocSubject>();
        foreach (var name in proto.AllMessageTypeNames())
        {
            var schema = proto.Schema(name);
            var fields = schema is null
                ? (IReadOnlyList<DocSubject>)[]
                : schema.Fields.Select(FieldSubject).ToList();
            list.Add(new DocSubject("message", name, name, null, fields));
        }
        return list;
    }

    private static DocSubject FieldSubject(SchemaField f)
    {
        // Type summary, e.g. "repeated Contract", "uint32", "enum". Messages show the inner type name.
        var typeText = f.Type == "message" && f.MessageType is not null ? f.MessageType : f.Type;
        var summary = f.Repeated ? $"repeated {typeText}" : typeText;
        return new DocSubject("field", f.Name, f.Name, summary, []);
    }

    // One subject per route. Summary names the request -> response types; children link to the
    // message subjects for any request/response type the proto reflection knows.
    private static IReadOnlyList<DocSubject> BuildEndpoints(IRouteCatalog routes, IProtoReflection proto)
    {
        var list = new List<DocSubject>();
        foreach (var r in routes.All())
        {
            var req = r.Request ?? (r.RequestWrapped ? "AuthenticatedMessage" : "(none)");
            var res = r.Response ?? r.RawResponse ?? (r.ResponseWrapped ? "AuthenticatedMessage" : "(none)");
            var summary = $"request {req} -> response {res}";

            var children = new List<DocSubject>();
            LinkMessage(children, "request", r.Request, proto);
            LinkMessage(children, "response", r.Response, proto);

            list.Add(new DocSubject("endpoint", r.Path, r.Path, summary, children));
        }
        return list;
    }

    // Adds a child pointing at a known message type, if the proto reflection resolves it.
    private static void LinkMessage(List<DocSubject> into, string role, string? typeName, IProtoReflection proto)
    {
        if (string.IsNullOrEmpty(typeName)) return;
        if (proto.Schema(typeName) is null) return;
        into.Add(new DocSubject("message", typeName, $"{role}: {typeName}", null, []));
    }

    private sealed record ConfigOption(string Key, string? Default, string Summary, string AppliesTo);

    // Curated from the README Configuration table. Not exhaustive; the load-bearing options.
    private static readonly ConfigOption[] ConfigOptions =
    [
        new("AppMode", "Local", "Local = full features; Hosted = capture + writes disabled (the public deploy)", "host"),
        new("EndpointsPath", "<app dir>/Endpoints", "Endpoints (response payloads) root", "host"),
        new("ContentRoot", "auto-resolved", "The dir holding RouteMap/ + Endpoints/ (rarely set)", "host"),
        new("HttpPort", "8080", "HTTP port when certs are present (overrides ASPNETCORE_URLS)", "host"),
        new("HttpsPort", "8443", "HTTPS port (only active when certs are present)", "host"),
        new("ConnectionStrings:Postgres", "unset", "When set, enables the Postgres data layer and applies migrations at startup", "data"),
        new("Discord:ClientId", "unset", "Discord OAuth app client id; auth wires when both id + secret are set and a DB is configured", "auth"),
        new("Discord:ClientSecret", "unset", "Discord OAuth app client secret", "auth"),
        new("Discord:AdminIds", "unset", "Comma-separated Discord ids auto-promoted to admin on login (bootstraps the first admin)", "auth"),
        new("Discord:BotToken", "unset", "When set, starts the optional Discord bot", "bot"),
        new("Discord:GuildId", "unset", "Optional; enables instant guild command registration for the bot", "bot"),
        new("SHARED_ROLE_ID", "unset", "Optional; snowflake of a role the bot self-assigns on Ready. Shared with EggLedger in the same stack. Falls back to Discord:SharedRoleId", "bot"),
        new("CapturePort", "8080", "Port the capture proxy listens on", "capture"),
        new("CapturePath", "<content root>/captures", "Directory the capture HAR is written to", "capture"),
        new("CaPath", "<CapturePath>/eggincognito-ca.cer", "The persisted capture root CA file", "capture"),
        new("EGG_INC_EID", "optional", "EID used to scrub captured/imported data and as the in-app capture default", "capture"),
        new("EGG_INC_API_SALT", "required for signing", "API signing phrase for the Inspector's Live API sends", "inspector"),
        new("RateLimiting:Enabled", "true", "Master switch for the built-in rate limiter; false makes it a no-op", "host"),
    ];

    private static IReadOnlyList<DocSubject> BuildConfig() =>
        ConfigOptions
            .Select(o => new DocSubject(
                "config",
                o.Key,
                o.Key,
                $"{o.Summary} (default: {o.Default ?? "unset"}; applies to: {o.AppliesTo})",
                []))
            .ToList();

    // Curated UI controls (the toggles/checkboxes the SPA exposes). Sparse by design.
    private static readonly DocSubject[] Controls =
    [
        new("control", "inspector-send-target", "Inspector send target", "Mock / Live API / Custom proxy send toggle", []),
        new("control", "custom-proxy-url", "Custom proxy URL", "Browser-direct send target; bypasses this server", []),
        new("control", "redaction-mode", "Redaction mode", "PII redaction for captured/imported data (off / blur / redact)", []),
        new("control", "overwrite-existing", "Overwrite existing", "HAR import overwrite-existing-endpoints checkbox", []),
        new("control", "capture-pause", "Capture pause", "Pause/resume the live capture stream", []),
    ];

    private static IReadOnlyList<DocSubject> BuildControls() => Controls;
}
