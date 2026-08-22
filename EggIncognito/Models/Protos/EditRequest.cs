namespace EggIncognito.Models.Protos;

public sealed record EditRequest(string? AppVersion, string? ClientVersion, string? Source, string? Build);
