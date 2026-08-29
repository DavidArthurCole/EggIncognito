namespace EggIncognito.Services.Inspector;

public enum InspectorTarget {
    Mock,
    LiveViaServer,
    LiveViaProxy
}

public enum EnvEditor {
    Text,
    Eid,
    Int,
    Version,
    Build,
    Code,
    Select,
    Bool
}

public enum EnvValueType {
    String,
    Number,
    Boolean
}

public enum DocSubjectKind {
    Endpoint,
    Message,
    Config,
    Control
}

public static class InspectorTargets {
    private const string LegacyMock = "mock";
    private const string LegacyReal = "real";
    private const string LegacyCustom = "custom";

    public static InspectorTarget Parse(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return InspectorTarget.Mock;
        string v = value.Trim();
        if (Enum.TryParse(v, ignoreCase: true, out InspectorTarget parsed)) return parsed;
        return v.ToLowerInvariant() switch {
            LegacyReal => InspectorTarget.LiveViaServer,
            LegacyCustom => InspectorTarget.LiveViaProxy,
            _ => InspectorTarget.Mock
        };
    }

    public static bool IsLive(InspectorTarget target) =>
        target is InspectorTarget.LiveViaServer or InspectorTarget.LiveViaProxy;

    public static string Label(InspectorTarget target) => target switch {
        InspectorTarget.LiveViaServer => "live via server",
        InspectorTarget.LiveViaProxy => "live via proxy",
        _ => "mock"
    };
}

public static class DocSubjectKinds {
    public const string Endpoint = "endpoint";
    public const string Message = "message";
    public const string Config = "config";
    public const string Control = "control";

    public static string Slug(DocSubjectKind kind) => kind switch {
        DocSubjectKind.Endpoint => Endpoint,
        DocSubjectKind.Config => Config,
        DocSubjectKind.Control => Control,
        _ => Message
    };

    public static DocSubjectKind Parse(string slug) => slug switch {
        Endpoint => DocSubjectKind.Endpoint,
        Config => DocSubjectKind.Config,
        Control => DocSubjectKind.Control,
        _ => DocSubjectKind.Message
    };
}
