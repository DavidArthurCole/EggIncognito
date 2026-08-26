namespace EggIncognito.Models.Admin;

public sealed record DataStatusFixtureRow(string Name, long Bytes, DateTimeOffset UpdatedAt, string Status);
