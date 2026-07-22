using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("feed_deliveries")]
public sealed class FeedDelivery {
    [Column("id")] public int Id { get; set; }
    [Column("subscription_id")] public int SubscriptionId { get; set; }
    [Column("event_kind")] public string EventKind { get; set; } = "proto_build";
    [Column("dedup_key")] public string DedupKey { get; set; } = "";
    [Column("status")] public string Status { get; set; } = "";
    [Column("attempted_at")] public DateTimeOffset AttemptedAt { get; set; }
    [Column("response_code")] public int? ResponseCode { get; set; }
    [Column("attempts")] public int Attempts { get; set; }
}
