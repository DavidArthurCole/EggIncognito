namespace EggIncognito.Models.Registry;

public sealed record LiveVersionDto(bool Found, string? Platform, string? Version, string? Build, int? ClientVersion);
