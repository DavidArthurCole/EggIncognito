namespace EggIncognito.Services.Workbench;

public abstract class WorkbenchStateBase {
#pragma warning disable IDE0032
    private string? _mode;
#pragma warning restore IDE0032

    public abstract IReadOnlyList<WorkbenchMode> Modes { get; }

    public virtual string DefaultMode => Modes.Count > 0 ? Modes[0].Key : "";

    public string Mode {
        get => _mode ?? DefaultMode;
        set => _mode = NormalizeMode(value);
    }

    public string RailFilter { get; set; } = "";

    public virtual string HashPrefix => "";

    public string NormalizeMode(string? mode) {
        if (mode is not { Length: > 0 }) return DefaultMode;
        foreach (var candidate in Modes) {
            if (string.Equals(candidate.Key, mode, StringComparison.Ordinal)) return mode;
        }

        return DefaultMode;
    }

    public bool OwnsHash(string? hash) {
        string prefix = HashPrefix;
        if (prefix.Length == 0) return false;
        string body = (hash ?? "").TrimStart('#');
        return string.Equals(body, prefix, StringComparison.Ordinal)
               || body.StartsWith(prefix + "_", StringComparison.Ordinal);
    }

    public virtual string? Hash() => null;

    public virtual bool ApplyHash(string? hash) => false;
}
