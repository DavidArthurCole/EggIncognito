using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;
[Table("backfill_jobs")]
public sealed class BackfillJob
{
    [Column("id")] public int Id { get; set; }
    [Column("source")] public string Source { get; set; } = "";
    [Column("status")] public string Status { get; set; } = "running";
    [Column("started_at")] public DateTimeOffset StartedAt { get; set; }
    [Column("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [Column("imported")] public int Imported { get; set; }
    [Column("total")] public int? Total { get; set; }
    [Column("note")] public string? Note { get; set; }
    [Column("started_by")] public string? StartedBy { get; set; }
}
