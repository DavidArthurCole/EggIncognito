using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("proto_canonicals")]
public sealed class ProtoCanonical {
    [Column("proto_sha")] public string ProtoSha { get; set; } = "";
    [Column("canonical_sha")] public string? CanonicalSha { get; set; }
    [Column("canonical_text")] public string? CanonicalText { get; set; }
    [Column("ok")] public bool Ok { get; set; }
    [Column("error")] public string? Error { get; set; }
    [Column("computed_at")] public DateTimeOffset ComputedAt { get; set; }
}
