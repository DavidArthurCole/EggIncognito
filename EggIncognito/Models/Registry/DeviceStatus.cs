namespace EggIncognito.Models.Registry;

public sealed record DeviceStatus(
    string Id,
    string Platform,
    string Label,
    bool Reachable,
    string? InstalledAppVersion,
    string? InstalledBuild,
    string? StoreLatest,
    bool StoreAhead,
    string Result,
    string? Note,
    DateTimeOffset ProbedAt,
    int? ClientVersion);
