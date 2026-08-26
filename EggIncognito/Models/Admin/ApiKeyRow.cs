namespace EggIncognito.Models.Admin;

public sealed record ApiKeyRow(long Id, string? OwnerUserId, string Name, string Prefix, DateTimeOffset? LastUsedAt, long RequestCount, bool Revoked);
