namespace EggIncognito.Models.AdminUi;

public record ApiKeyRow(
    long Id,
    string Name,
    string Prefix,
    string OwnerUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    long RequestCount,
    bool Revoked,
    DateTimeOffset? RevokedAt);
