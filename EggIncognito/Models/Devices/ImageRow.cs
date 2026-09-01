namespace EggIncognito.Models.Devices;

public sealed record ImageRow(
    string? Tag,
    IReadOnlyList<string> RepoTags,
    string Id,
    long Size,
    DateTimeOffset Created,
    bool Active);
