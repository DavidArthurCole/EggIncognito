using EggIncognito.Components.Capture;
using EggIncognito.Services.Workbench;

namespace EggIncognito.Services.Data;

public sealed record DataPayloadEntry(int Status, string Text);

public sealed class DataWorkbenchState : WorkbenchStateBase {
    public const string ModeData = "data";
    public const string ModeAbout = "about";

    private const string Prefix = "data/";

    public override IReadOnlyList<WorkbenchMode> Modes { get; } = [
        new WorkbenchMode(ModeData, "Data"),
        new WorkbenchMode(ModeAbout, "About")
    ];

    public string Group { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Sub { get; set; }

#pragma warning disable IDE0028
    public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Formats { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DataPayloadEntry> Payloads { get; } = new(StringComparer.Ordinal);
#pragma warning restore IDE0028
    public CaptureViewState View { get; } = new();

    public bool HasSelection => Id.Length > 0 && Group.Length > 0;

    public string Key => Sub is { Length: > 0 } sub ? $"{Group}/{Id}/{sub}" : $"{Group}/{Id}";

    public void Select(string group, string id, string? sub) {
        Group = group;
        Id = id;
        Sub = string.IsNullOrEmpty(sub) ? null : sub;
    }

    public string NameFor(string key) => Names.GetValueOrDefault(key, "");

    public static bool IsMode(string? value) =>
        string.Equals(value, ModeData, StringComparison.Ordinal) ||
        string.Equals(value, ModeAbout, StringComparison.Ordinal);

    public override string? Hash() {
        if (!HasSelection) return null;
        string body = Sub is { Length: > 0 } sub
            ? $"{Prefix}{Group}/{Id}/{sub}"
            : $"{Prefix}{Group}/{Id}";
        return Mode == DefaultMode ? body : $"{body}.{Mode}";
    }

    public override bool ApplyHash(string? hash) {
        (bool match, string group, string id, string? sub, string mode) = ParseHash(hash);
        if (!match) return false;
        Select(group, id, sub);
        Mode = mode;
        return true;
    }

    public static (bool Match, string Group, string Id, string? Sub, string Mode) ParseHash(string? hash) {
        (bool, string, string, string?, string) no = (false, "", "", null, ModeData);
        string body = (hash ?? "").TrimStart('#');
        if (!body.StartsWith(Prefix, StringComparison.Ordinal)) return no;

        string rest = body[Prefix.Length..];
        if (rest.Length == 0) return no;

        string mode = ModeData;
        int dot = rest.LastIndexOf('.');
        if (dot >= 0) {
            string candidate = rest[(dot + 1)..];
            if (!IsMode(candidate)) return no;
            mode = candidate;
            rest = rest[..dot];
        }

        string[] parts = rest.Split('/');
        if (parts.Length is < 2 or > 3) return no;
        foreach (string part in parts) {
            if (part.Length == 0 || part.Contains('.', StringComparison.Ordinal)) return no;
        }

        return (true, parts[0], parts[1], parts.Length == 3 ? parts[2] : null, mode);
    }
}
