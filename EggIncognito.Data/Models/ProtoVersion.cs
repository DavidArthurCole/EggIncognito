using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One detected game build's registry metadata, keyed by (platform, version). The .proto text itself
// lives in ProtoProto. Unique on (platform, version): re-ingesting the same build updates this row.
[Table("proto_versions")]
public sealed class ProtoVersion
{
    [Column("id")] public int Id { get; set; }
    [Column("platform")] public string Platform { get; set; } = "android";
    [Column("version")] public string Version { get; set; } = "";
    [Column("package")] public string Package { get; set; } = "";
    [Column("proto_sha")] public string ProtoSha { get; set; } = "";
    [Column("apk_ref")] public string ApkRef { get; set; } = "";
    [Column("detected_at")] public DateTimeOffset DetectedAt { get; set; }
    [Column("detected_by")] public string? DetectedBy { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
}
