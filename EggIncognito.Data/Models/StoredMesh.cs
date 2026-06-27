using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// A decoded game mesh (.glb bytes) cached in Postgres, keyed by (platform, stem). Pulling a mesh off a
// device is slow + needs the device online; caching the decoded glb here means every instance reuses one
// pull and a device is only touched for a stem not yet stored. The DB cache is the durable shared layer;
// the on-disk MeshAssetCache stays as a fast local mirror. Assets remain Auxbrain's property; this only
// stores a reformatted copy already present on a device the operator controls.
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
