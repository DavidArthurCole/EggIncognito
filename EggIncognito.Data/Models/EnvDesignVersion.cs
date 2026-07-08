using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One saved version of an environment design; every save appends a row with a monotonic VersionNo per design.
// The parent EnvDesign holds the head (latest) payload for fast load; this table is the timeline behind it.
[Table("env_design_versions")]
public class EnvDesignVersion
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("design_id")]
    public long DesignId { get; set; }

    [Column("version_no")]
    public int VersionNo { get; set; }

    [Column("payload", TypeName = "jsonb")]
    public string Payload { get; set; } = "{}";

    [Column("author_user_id")]
    public string? AuthorUserId { get; set; }

    [Column("note")]
    public string? Note { get; set; }

    // The version restored by rollback, if this version was created by one. Null otherwise.
    [Column("rolled_back_from")]
    public int? RolledBackFrom { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
