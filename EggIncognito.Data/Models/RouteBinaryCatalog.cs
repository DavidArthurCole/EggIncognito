using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("route_binary_catalog")]
public class RouteBinaryCatalog {
    [Key][Column("path")] public string Path { get; set; } = "";

    [Column("method")] public string? Method { get; set; }

    [Column("request_type")] public string? RequestType { get; set; }

    [Column("response_type")] public string? ResponseType { get; set; }

    [Column("request_wrapped")] public bool RequestWrapped { get; set; }

    [Column("response_wrapped")] public bool ResponseWrapped { get; set; }

    [Column("binary_version")] public string? BinaryVersion { get; set; }

    [Column("refreshed_at")] public DateTimeOffset RefreshedAt { get; set; }
}
