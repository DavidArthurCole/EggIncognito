namespace EggIncognito.Models.Admin;

public sealed record GameDataDocRow(string Id, bool Present, DateTimeOffset? UpdatedAt, int? Bytes);
