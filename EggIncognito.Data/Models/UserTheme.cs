using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("user_themes")]
public sealed class UserTheme {
    [Column("id")] public long Id { get; set; }
    [Column("owner_user_id")] public Guid OwnerUserId { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("slug")] public string Slug { get; set; } = "";
    [Column("schema_version")] public int SchemaVersion { get; set; }
    [Column("model")] public string Model { get; set; } = "";
    [Column("is_active")] public bool IsActive { get; set; }
    [Column("validated_at")] public DateTimeOffset? ValidatedAt { get; set; }
    [Column("validation")] public string? Validation { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}
