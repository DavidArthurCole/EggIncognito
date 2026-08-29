namespace EggIncognito.Models.Devices;

public sealed record UiNodeRow(
    int Id,
    int Depth,
    string Kind,
    string Label,
    string? ResourceId,
    string? Text,
    string? ContentDesc,
    string? ClassName,
    string? Package,
    int Left,
    int Top,
    int Right,
    int Bottom,
    bool Clickable,
    bool Enabled,
    IReadOnlyList<UiSelectorHint> Selectors) {
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => (Left + Right) / 2;
    public int CenterY => (Top + Bottom) / 2;
    public bool HasBounds => Width > 0 && Height > 0;
}
