using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("device_updates")]
public sealed class DeviceUpdate {
    [Column("id")] public int Id { get; set; }
    [Column("device_id")] public string DeviceId { get; set; } = "";
    [Column("attempted_at")] public DateTimeOffset AttemptedAt { get; set; }
    [Column("from_version")] public string? FromVersion { get; set; }
    [Column("to_version")] public string? ToVersion { get; set; }
    [Column("status")] public string Status { get; set; } = "";
    [Column("note")] public string? Note { get; set; }
    [Column("triggered_by")] public string TriggeredBy { get; set; } = "auto";
}
