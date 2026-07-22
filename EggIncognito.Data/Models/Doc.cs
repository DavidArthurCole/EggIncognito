using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("docs")]
public class Doc {
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
