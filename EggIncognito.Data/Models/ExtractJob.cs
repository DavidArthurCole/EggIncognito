using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("extract_jobs")]
public sealed class ExtractJob {
    [Column("id")] public int Id { get; set; }
    [Column("platform")] public string Platform { get; set; } = "android";
    [Column("app_version")] public string AppVersion { get; set; } = "";
    [Column("status")] public string Status { get; set; } = "queued";
    [Column("started_at")] public DateTimeOffset StartedAt { get; set; }
    [Column("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [Column("note")] public string? Note { get; set; }
}
