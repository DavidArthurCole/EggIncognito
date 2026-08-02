using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("upload_batches")]
public sealed class UploadBatch {
    [Key][Column("id")] public int Id { get; set; }
    [Column("submitted_by")] public string? SubmittedBy { get; set; }
    [Column("submitted_at")] public DateTimeOffset SubmittedAt { get; set; }
    [Column("status")] public string Status { get; set; } = "pending";
    [Column("total_items")] public int TotalItems { get; set; }
    [Column("processed_items")] public int ProcessedItems { get; set; }
    [Column("note")] public string? Note { get; set; }
}
