namespace EggIncognito.Models.Protos;

public sealed record SaveRequest(
    string Platform,
    string AppVersion,
    string Build,
    string? ClientVersion,
    string? Package,
    string? Proto,
    string? Source);
