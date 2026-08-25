using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("game_events")]
public sealed class GameEvent {
    [Key][Column("id")] public long Id { get; set; }

    [Column("event_id")] public string EventId { get; set; } = "";

    [Column("event_type")] public string EventType { get; set; } = "";

    [Column("message")] public string Message { get; set; } = "";

    [Column("multiplier")] public double Multiplier { get; set; }

    [Column("ultra")] public bool Ultra { get; set; }

    [Column("start_time")] public DateTimeOffset StartTime { get; set; }

    [Column("end_time")] public DateTimeOffset EndTime { get; set; }

    [Column("source")] public string Source { get; set; } = "";

    [Column("first_seen_at")] public DateTimeOffset? FirstSeenAt { get; set; }

    [Column("last_seen_at")] public DateTimeOffset? LastSeenAt { get; set; }
}
