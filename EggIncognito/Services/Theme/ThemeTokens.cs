namespace EggIncognito.Services.Theme;

public static class ThemeTokens {
    public static readonly IReadOnlyList<string> Settable = [
        "bg", "panel0", "panel", "panel2", "fg", "muted", "accent", "info", "ok", "err", "border"
    ];

    public static readonly IReadOnlyList<string> Derived = [
        "--egi-glow", "--egi-panel-tint", "--egi-accent-grad-to", "--egi-hue-shift"
    ];

    public static readonly IReadOnlyList<string> Locked = [
        "accent2", "warn", "border-strong", "border-input", "control-border", "fg-soft", "fg-max",
        "panel-hi", "panel-lo", "panel-glow", "popover", "scrim", "row-alt", "surface-tint",
        "token-literal", "method-read", "method-write", "method-replace", "method-remove",
        "diff-add-bg", "diff-del-bg", "err-soft", "separator", "scroll-track", "scroll-thumb",
        "scroll-thumb-hover"
    ];

    private static readonly HashSet<string> SettableSet = new(Settable, StringComparer.Ordinal);
    private static readonly HashSet<string> LockedSet = new(Locked, StringComparer.Ordinal);

    public static bool IsSettable(string name) => SettableSet.Contains(name);

    public static bool IsLocked(string name) => LockedSet.Contains(name);

    public static string? CanonicalSettable(string name) =>
        SettableSet.TryGetValue(name, out string? canonical) ? canonical : null;
}
