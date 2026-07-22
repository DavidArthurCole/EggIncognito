using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("api_keys")]
public sealed class ApiKey {
    [Column("id")] public int Id { get; set; }
    [Column("key_hash")] public string KeyHash { get; set; } = "";
    [Column("prefix")] public string Prefix { get; set; } = "";
    [Column("name")] public string Name { get; set; } = "";
    [Column("owner_user_id")] public Guid OwnerUserId { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("last_used_at")] public DateTimeOffset? LastUsedAt { get; set; }
    [Column("request_count")] public long RequestCount { get; set; }
    [Column("revoked")] public bool Revoked { get; set; }
    [Column("revoked_at")] public DateTimeOffset? RevokedAt { get; set; }
}
