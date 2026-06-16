using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One detected game build's registry metadata, keyed by (platform, build). The .proto text itself
// lives in ProtoProto. Unique on (platform, build): re-ingesting the same build updates this row.
// A build carries three version numbers (all needed for downstream API calls): app_version is the
// user-facing label (not unique), build is the monotonic versionCode (the row key), client_version
// is the proto/API client version (best-effort, nullable until extracted from the binary). source
// records where the build came from ("farm" default; later backfill uses elgranjero/playstore/etc).
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
    // Soft-delete: hidden from the default list, not physically removed (auto-importers would resurrect a
    // hard-deleted build). A deleted row stays deleted through re-ingest until an admin restores it.
    [Column("deleted_at")] public DateTimeOffset? DeletedAt { get; set; }
    // Merge: when set, this row is an alias of the canonical ProtoVersion it points to (same schema,
    // e.g. an iOS + Android build sharing a proto). Aliases are soft-deleted (hidden) but reversible.
    [Column("canonical_id")] public int? CanonicalId { get; set; }
}
