using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("feed_suppressions")]
public sealed class FeedSuppression {
    [Column("id")] public int Id { get; set; }
    [Column("subscription_id")] public int SubscriptionId { get; set; }
    [Column("event_kind")] public string EventKind { get; set; } = "proto_build";
    [Column("dedup_key")] public string DedupKey { get; set; } = "";
    [Column("reason")] public string Reason { get; set; } = "";
    [Column("summary")] public string? Summary { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
}
