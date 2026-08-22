namespace EggIncognito.Models.Registry;

public sealed record RegRowDto(string Platform, string AppVersion, string Build, string? ClientVersion);
