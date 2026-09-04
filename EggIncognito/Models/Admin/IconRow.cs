namespace EggIncognito.Models.Admin;

public sealed record IconRow(string Name, string Platform, long Bytes, string ContentType, string Sha256, DateTimeOffset UpdatedAt);
