using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;
[Table("stored_meshes")]
public class StoredMesh
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("platform")]
    public string Platform { get; set; } = "";

    [Column("stem")]
    public string Stem { get; set; } = "";

    [Column("glb")]
    public byte[] Glb { get; set; } = [];

    [Column("byte_size")]
    public int ByteSize { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
