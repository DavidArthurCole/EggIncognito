using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("proto_sha_orders")]
public sealed class ProtoShaOrder {
    [Column("proto_sha")] public string ProtoSha { get; set; } = "";
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [Column("updated_by")] public string? UpdatedBy { get; set; }
}
