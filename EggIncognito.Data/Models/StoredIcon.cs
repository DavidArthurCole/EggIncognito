using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("stored_icons")]
public class StoredIcon {
    [Key][Column("id")] public long Id { get; set; }

    [Column("name")] public string Name { get; set; } = "";

    [Column("content_type")] public string ContentType { get; set; } = "image/png";

    [Column("bytes")] public byte[] Bytes { get; set; } = [];

    [Column("byte_size")] public int ByteSize { get; set; }

    [Column("provenance")] public string Provenance { get; set; } = "";

    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
}
