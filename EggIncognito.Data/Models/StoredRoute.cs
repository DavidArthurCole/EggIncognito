using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// A route definition mirrored into Postgres. source="yaml" rows are seeded from the compiled route catalog on boot; source="db" rows are user-added paths the dynamic controller serves at runtime.
[Table("stored_routes")]
public class StoredRoute
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("path")]
    public string Path { get; set; } = "";

    [Column("request_type")]
    public string? RequestType { get; set; }

    [Column("response_type")]
    public string? ResponseType { get; set; }

    [Column("request_wrapped")]
    public bool RequestWrapped { get; set; }

    [Column("response_wrapped")]
    public bool ResponseWrapped { get; set; }

    [Column("raw_response")]
    public string? RawResponse { get; set; }

    [Column("path_param")]
    public bool PathParam { get; set; }

    [Column("path_param_only")]
    public bool PathParamOnly { get; set; }

    [Column("source")]
    public string Source { get; set; } = "yaml";

    [Column("owner_user_id")]
    public Guid? OwnerUserId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
