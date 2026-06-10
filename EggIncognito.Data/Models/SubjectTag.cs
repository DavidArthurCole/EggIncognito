using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// Join row linking a subject, message type or endpoint path, to a Tag. Same subject model as Doc so
// endpoints and messages tag uniformly. No hard FK to tags.id, matching the codebase's no-FK
// convention; the app enforces validity. Unique per (subject, tag) so a tag is applied at most once.
[Table("subject_tags")]
public class SubjectTag
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("subject_kind")]
    public string SubjectKind { get; set; } = "";

    [Column("subject_key")]
    public string SubjectKey { get; set; } = "";

    [Column("tag_id")]
    public long TagId { get; set; }
}
