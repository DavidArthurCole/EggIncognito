using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One backfill run's progress, so the admin UI can show status without tailing logs. The importer
// creates a row at start (status running), bumps imported as it goes, then finishes done/failed.
[Table("backfill_jobs")]
public sealed class BackfillJob
{
    [Column("id")] public int Id { get; set; }
    [Column("source")] public string Source { get; set; } = ""; // elgranjero|fandom|uptodown|apkpure|itunes|apk-extract
    [Column("status")] public string Status { get; set; } = "running"; // running|done|failed
    [Column("started_at")] public DateTimeOffset StartedAt { get; set; }
    [Column("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [Column("imported")] public int Imported { get; set; }
    [Column("total")] public int? Total { get; set; }
    [Column("note")] public string? Note { get; set; }
    [Column("started_by")] public string? StartedBy { get; set; }
}
