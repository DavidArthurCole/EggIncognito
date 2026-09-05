namespace EggIncognito.Models.Devices;

public sealed record DeviceRow(
    string Id,
    string Platform,
    string Label,
    string? Target,
    string? Package,
    bool Reachable,
    string? InstalledAppVersion,
    string? InstalledBuild,
    string? StoreLatest,
    bool StoreAhead,
    string Result,
    string? Note,
    DateTimeOffset ProbedAt,
    int? ClientVersion,
    bool Virtual = false,
    DateTimeOffset? Up = null,
    int CapturePort = 0);
