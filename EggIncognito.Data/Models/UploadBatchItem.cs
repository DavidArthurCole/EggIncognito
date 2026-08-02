using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("upload_batch_items")]
public sealed class UploadBatchItem {
    [Key][Column("id")] public int Id { get; set; }
    [Column("batch_id")] public int BatchId { get; set; }
    [Column("file_name")] public string FileName { get; set; } = "";
    [Column("size_bytes")] public long SizeBytes { get; set; }
    [Column("bytes")] public byte[]? Bytes { get; set; }
    [Column("status")] public string Status { get; set; } = "pending";
    [Column("platform")] public string? Platform { get; set; }
    [Column("proto_sha")] public string? ProtoSha { get; set; }
    [Column("app_version")] public string? AppVersion { get; set; }
    [Column("build")] public string? Build { get; set; }
    [Column("client_version")] public string? ClientVersion { get; set; }
    [Column("diagnostics")] public string? Diagnostics { get; set; }
    [Column("processed_at")] public DateTimeOffset? ProcessedAt { get; set; }
}
