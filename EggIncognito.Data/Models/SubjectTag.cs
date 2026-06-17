using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// Links a subject (message type or endpoint path) to a Tag. No hard FK to tags; validity enforced in app. Unique per (subject, tag).
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
