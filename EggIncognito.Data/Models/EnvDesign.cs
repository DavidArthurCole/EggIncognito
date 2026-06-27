using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// A named environment design authored in the playground designer: the JSON payload holds the placed elements,
// lighting, and background. Stored opaque (the client owns its shape); the server only validates well-formed
// JSON + a size cap. Shared via the DB so designs survive redeploys + are visible across instances. No asset
// bytes here (meshes are pulled + cached separately); a design only references stems/identifiers.
[Table("env_designs")]
public class EnvDesign
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("payload", TypeName = "jsonb")]
    public string Payload { get; set; } = "{}";

    [Column("owner_user_id")]
    public string? OwnerUserId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
