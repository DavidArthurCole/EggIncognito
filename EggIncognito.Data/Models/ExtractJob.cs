using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One per-version APK extract run. Unique on (platform, app_version): a re-extract resets the existing row to running.
[Table("extract_jobs")]
public sealed class ExtractJob
{
    [Column("id")] public int Id { get; set; }
    [Column("platform")] public string Platform { get; set; } = "android";
    [Column("app_version")] public string AppVersion { get; set; } = "";
    [Column("status")] public string Status { get; set; } = "queued"; // queued|running|done|failed
    [Column("started_at")] public DateTimeOffset StartedAt { get; set; }
    [Column("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [Column("note")] public string? Note { get; set; } // error message or resulting build/sha
}
