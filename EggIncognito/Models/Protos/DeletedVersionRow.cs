namespace EggIncognito.Models.Protos;

public sealed record DeletedVersionRow(
    string Platform,
    string Build,
    string AppVersion,
    string? ClientVersion,
    string? Source,
    string? ProtoSha,
    DateTimeOffset? DeletedAt);
