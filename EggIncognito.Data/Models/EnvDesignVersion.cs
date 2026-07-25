using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("env_design_versions")]
public class EnvDesignVersion {
    [Key][Column("id")] public long Id { get; set; }

    [Column("design_id")] public long DesignId { get; set; }

    [Column("version_no")] public int VersionNo { get; set; }

    [Column("payload", TypeName = "jsonb")]
    public string Payload { get; set; } = "{}";

    [Column("author_user_id")] public Guid? AuthorUserId { get; set; }

    [Column("note")] public string? Note { get; set; }


    [Column("rolled_back_from")] public int? RolledBackFrom { get; set; }

    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
}
