namespace EggIncognito.Models.AdminUi;

public record RouteBinaryRow(
    string Path,
    string? Method,
    string? Request,
    string? Response,
    bool RequestWrapped,
    bool ResponseWrapped,
    string? BinaryVersion,
    DateTimeOffset RefreshedAt);
