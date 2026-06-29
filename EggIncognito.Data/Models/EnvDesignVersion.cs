using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One saved version of an environment design. Every save appends a row (monotonic VersionNo per design), so a
// design carries its full edit history and the user can roll back to any prior version. The parent EnvDesign
// holds the head payload (the latest) for fast load + listing; this table is the timeline behind it. Payload is
// opaque app JSON, same contract + size cap as EnvDesign. Rollback appends a new version copying an old one.
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

    // A short user-facing note for the version (e.g. "added second hatchery"), optional.
    [Column("note")]
    public string? Note { get; set; }

    // When a version was created by rolling back, the version it restored (for an audit trail). Null otherwise.
    [Column("rolled_back_from")]
    public int? RolledBackFrom { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
