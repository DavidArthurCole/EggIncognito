using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("doc_images")]
public class DocImage {
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("content_type")]
    public string ContentType { get; set; } = "";

    [Column("bytes")]
    public byte[] Bytes { get; set; } = [];

    [Column("byte_size")]
    public int ByteSize { get; set; }

    [Column("owner_user_id")]
    public Guid? OwnerUserId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
