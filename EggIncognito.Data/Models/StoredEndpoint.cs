using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// A user-contributed endpoint response stored in Postgres, overlaying the file default for its (path, eid) key. Eid null means global.
[Table("stored_endpoints")]
public class StoredEndpoint
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("path")]
    public string Path { get; set; } = "";

    [Column("eid")]
    public string? Eid { get; set; }

    [Column("response_json")]
    public string ResponseJson { get; set; } = "";

    [Column("response_type")]
    public string ResponseType { get; set; } = "";

    [Column("owner_user_id")]
    public string? OwnerUserId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
