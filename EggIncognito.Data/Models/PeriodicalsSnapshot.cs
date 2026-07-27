using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("periodicals_snapshots")]
public class PeriodicalsSnapshot {
    [Key][Column("id")] public long Id { get; set; }

    [Column("captured_at")] public DateTimeOffset CapturedAt { get; set; }

    [Column("sha")] public string Sha { get; set; } = "";

    [Column("response_json")] public string ResponseJson { get; set; } = "";
}
