using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// Editable documentation about an API subject: subject_kind is "message" (subject_key = Ei.* short type name) or "endpoint" (subject_key = route path), one doc per subject.
// Body is Markdown, HTML-escaped on render so a contributor cannot inject script.
[Table("docs")]
public class Doc
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("subject_kind")]
    public string SubjectKind { get; set; } = "";

    [Column("subject_key")]
    public string SubjectKey { get; set; } = "";

    [Column("body_md")]
    public string BodyMd { get; set; } = "";

    [Column("owner_user_id")]
    public Guid? OwnerUserId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
