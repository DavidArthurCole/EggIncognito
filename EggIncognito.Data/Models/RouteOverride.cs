using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("route_overrides")]
public class RouteOverride {
    [Key][Column("path")] public string Path { get; set; } = "";

    [Column("request_type")] public string? RequestType { get; set; }

    [Column("response_type")] public string? ResponseType { get; set; }

    [Column("request_wrapped")] public bool? RequestWrapped { get; set; }

    [Column("response_wrapped")] public bool? ResponseWrapped { get; set; }

    [Column("path_param")] public bool? PathParam { get; set; }

    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }

    [Column("updated_by")] public Guid? UpdatedBy { get; set; }
}
