using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("env_designs")]
public class EnvDesign {
    [Key][Column("id")] public long Id { get; set; }

    [Column("name")] public string Name { get; set; } = "";

    [Column("payload", TypeName = "jsonb")]
    public string Payload { get; set; } = "{}";

    [Column("owner_user_id")] public Guid? OwnerUserId { get; set; }

    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}
