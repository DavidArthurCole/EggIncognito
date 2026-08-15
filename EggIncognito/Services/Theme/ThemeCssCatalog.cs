namespace EggIncognito.Services.Theme;

public enum ThemePropertyGroup {
    ColorOnly,
    Surface,
    Text,
    Full
}

public sealed record ThemeCatalogEntry(string Name, string Selector, ThemePropertyGroup Group);

public static class ThemeCssCatalog {
    public static readonly IReadOnlyList<ThemeCatalogEntry> Entries = [
        new("panel", ".panel", ThemePropertyGroup.Surface),
        new("panel-title", ".panel h2", ThemePropertyGroup.Text),
        new("button", ".btn-mini", ThemePropertyGroup.Full),
        new("button-hover", ".btn-mini:hover", ThemePropertyGroup.Full),
        new("button-primary", ".btn-primary", ThemePropertyGroup.Full),
        new("button-primary-hover", ".btn-primary:hover:not(:disabled)", ThemePropertyGroup.Full),
        new("button-secondary", ".btn-secondary", ThemePropertyGroup.Full),
        new("input", ".reg-filter-input, .protos-filter", ThemePropertyGroup.Full),
        new("input-focus", ".reg-filter-input:focus, .protos-filter:focus", ThemePropertyGroup.Full),
        new("table", ".data-table", ThemePropertyGroup.Surface),
        new("table-header", ".data-table th", ThemePropertyGroup.Text),
        new("table-row", ".data-table tbody tr", ThemePropertyGroup.Surface),
        new("table-row-alt", ".data-table tbody tr:nth-child(even)", ThemePropertyGroup.Surface),
        new("link", ".page a", ThemePropertyGroup.Text),
        new("link-hover", ".page a:hover", ThemePropertyGroup.Text),
        new("code", ".code-chip", ThemePropertyGroup.Text),
        new("scrollbar-thumb", "::-webkit-scrollbar-thumb", ThemePropertyGroup.ColorOnly),
        new("scrollbar-track", "::-webkit-scrollbar-track", ThemePropertyGroup.ColorOnly),
        new("workbench-rail", ".wb-rail", ThemePropertyGroup.Surface),
        new("workbench-entry", ".wb-entry", ThemePropertyGroup.Surface),
        new("workbench-entry-selected", ".wb-entry.selected", ThemePropertyGroup.Surface),
        new("modal-card", ".modal-card", ThemePropertyGroup.Surface),
        new("nav", ".app-nav", ThemePropertyGroup.ColorOnly),
        new("nav-item", ".app-nav a", ThemePropertyGroup.ColorOnly)
    ];

    private static readonly Dictionary<string, ThemeCatalogEntry> ByName =
        Entries.ToDictionary(e => e.Name, StringComparer.Ordinal);

    private static readonly Dictionary<string, ThemePropertyGroup> PropertyFloor = new(StringComparer.Ordinal) {
        ["color"] = ThemePropertyGroup.ColorOnly,
        ["background-color"] = ThemePropertyGroup.ColorOnly,
        ["border-color"] = ThemePropertyGroup.ColorOnly,
        ["border-width"] = ThemePropertyGroup.Surface,
        ["border-style"] = ThemePropertyGroup.Surface,
        ["border-radius"] = ThemePropertyGroup.Surface,
        ["box-shadow"] = ThemePropertyGroup.Surface,
        ["background-image"] = ThemePropertyGroup.Surface,
        ["font-weight"] = ThemePropertyGroup.Text,
        ["font-style"] = ThemePropertyGroup.Text,
        ["letter-spacing"] = ThemePropertyGroup.Text,
        ["text-transform"] = ThemePropertyGroup.Text,
        ["text-decoration-line"] = ThemePropertyGroup.Text,
        ["text-decoration-color"] = ThemePropertyGroup.Text,
        ["transition-duration"] = ThemePropertyGroup.Full,
        ["transition-property"] = ThemePropertyGroup.Full,
        ["outline-color"] = ThemePropertyGroup.Full,
        ["caret-color"] = ThemePropertyGroup.Full,
        ["accent-color"] = ThemePropertyGroup.Full,
        ["opacity"] = ThemePropertyGroup.Full
    };

    public static ThemeCatalogEntry? Find(string name) =>
        ByName.TryGetValue(name, out var entry) ? entry : null;

    public static string? CanonicalProperty(string name) =>
        PropertyFloor.TryGetValue(name, out _)
            ? PropertyFloor.Keys.First(k => string.Equals(k, name, StringComparison.Ordinal))
            : null;

    public static bool Allows(ThemePropertyGroup group, string canonicalProperty) =>
        PropertyFloor.TryGetValue(canonicalProperty, out var floor) && floor <= group;
}
