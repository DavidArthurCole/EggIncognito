using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// A piece of editable documentation about an API subject: either a proto message type
// (subject_kind="message", subject_key = the Ei.* short type name) or an endpoint
// (subject_kind="endpoint", subject_key = the route path). One doc per subject, unique index. Body is
// Markdown source; the SPA renders it HTML-escaped first, so a contributor cannot inject script.
// Contributor+ writes; public reads.
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
    public string? OwnerUserId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
