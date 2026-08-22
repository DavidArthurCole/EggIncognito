namespace EggIncognito.Models.Registry;

public sealed record VersionComboItem(
    string Value,
    string Platform,
    string? App,
    string? Build,
    string? Client,
    string? Sha,
    bool Dim,
    string? Note);
