using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("image_builds")]
public sealed class ImageBuild {
    [Key][Column("id")] public long Id { get; set; }

    [Column("spec")] public string Spec { get; set; } = "";

    [Column("tag")] public string Tag { get; set; } = "";

    [Column("state")] public string State { get; set; } = "queued";

    [Column("note")] public string? Note { get; set; }

    [Column("log")] public string Log { get; set; } = "";

    [Column("started_at")] public DateTimeOffset StartedAt { get; set; }

    [Column("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
}
