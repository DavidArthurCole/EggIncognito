namespace EggIncognito.Services.Assets;

public static class EventPalette {
    public const string Fallback = "#9ca3af";
    public const string CcGradientFrom = "#f5a709";
    public const string CcGradientTo = "#900fb1";

    private static readonly Dictionary<string, string> ByType = new(StringComparer.OrdinalIgnoreCase) {
        ["epic-research-sale"] = "#ef4444",
        ["piggy-boost"] = "#f97316",
        ["piggy-cap-boost"] = "#f59e0b",
        ["prestige-boost"] = "#f59e0b",
        ["earnings-boost"] = "#84cc16",
        ["gift-boost"] = "#10b981",
        ["drone-boost"] = "#10b981",
        ["research-sale"] = "#14b8a6",
        ["hab-sale"] = "#06b6d4",
        ["vehicle-sale"] = "#0ea5e9",
        ["boost-sale"] = "#3b82f6",
        ["boost-duration"] = "#6366f1",
        ["crafting-sale"] = "#8b5cf6",
        ["mission-fuel"] = "#8b5cf6",
        ["mission-capacity"] = "#d946ef",
        ["mission-duration"] = "#ec4899",
        ["shell-sale"] = "#f43f5e"
    };

    public static string ColorFor(string? eventType) =>
        string.IsNullOrEmpty(eventType)
            ? Fallback
            : ByType.GetValueOrDefault(eventType.Replace('_', '-'), Fallback);

    public static string? IconUrl(string? eventType) =>
        string.IsNullOrEmpty(eventType) || !eventType.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            ? null
            : $"/api/v1/data/asset/event-icon?name={Uri.EscapeDataString(eventType)}";
}
