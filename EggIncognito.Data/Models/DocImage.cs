using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// A binary image uploaded for use inside a Doc's Markdown, referenced by URL /api/docs/image/{id}.
// Stored as bytea (no filesystem writes); no hard FK to a doc, so an image is addressable on its own and may be reused.
[Table("doc_images")]
public class DocImage
{
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
    public string? OwnerUserId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
