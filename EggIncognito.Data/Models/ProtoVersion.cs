using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One game build in the registry, keyed by (platform, build). Proto text lives in ProtoProto.
// app_version is the user label, build is the monotonic versionCode row key, client_version is the nullable proto API version.
[Table("proto_versions")]
public sealed class ProtoVersion
{
    [Column("id")] public int Id { get; set; }
    [Column("platform")] public string Platform { get; set; } = "android";
    [Column("app_version")] public string AppVersion { get; set; } = "";
    [Column("build")] public string Build { get; set; } = "";
    [Column("client_version")] public string? ClientVersion { get; set; }
    [Column("source")] public string Source { get; set; } = "farm";
    [Column("package")] public string Package { get; set; } = "";
    [Column("proto_sha")] public string ProtoSha { get; set; } = "";
    [Column("apk_ref")] public string ApkRef { get; set; } = "";
    [Column("detected_at")] public DateTimeOffset DetectedAt { get; set; }
    [Column("detected_by")] public string? DetectedBy { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    // Soft-delete: hidden from the default list, not physically removed, so a re-ingest can't resurrect it.
    [Column("deleted_at")] public DateTimeOffset? DeletedAt { get; set; }
    // When set, this row is an alias of the canonical ProtoVersion it points to (e.g. iOS + Android sharing a proto).
    [Column("canonical_id")] public int? CanonicalId { get; set; }
}
