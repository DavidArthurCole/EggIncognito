namespace EggIncognito.Services.DataApi;

public sealed record ConfigFeed(string Id, string Label);

public static class ConfigFeeds {
    public const string Periodicals = "periodicals";
    public const string Config = "config";
    public const string Afx = "afx-config";
    public const string Seasons = "season-infos";

    public static readonly IReadOnlyList<ConfigFeed> All = [
        new(Periodicals, "Periodicals"),
        new(Config, "Game config"),
        new(Afx, "Artifacts config"),
        new(Seasons, "Season infos")
    ];

    public static ConfigFeed? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));

    public static string LabelOf(string? id) => ById(id)?.Label ?? id ?? "";
}
