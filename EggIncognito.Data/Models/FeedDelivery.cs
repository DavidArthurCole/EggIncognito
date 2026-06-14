using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One delivery attempt of one proto event to one subscription. Unique on (subscription, proto_version)
// so a retried ingest delivers at most once per sub.
[Table("feed_deliveries")]
public sealed class FeedDelivery
{
    [Column("id")] public int Id { get; set; }
    [Column("subscription_id")] public int SubscriptionId { get; set; }
    [Column("proto_version_id")] public int ProtoVersionId { get; set; }
    [Column("status")] public string Status { get; set; } = ""; // sent | failed
    [Column("attempted_at")] public DateTimeOffset AttemptedAt { get; set; }
    [Column("response_code")] public int? ResponseCode { get; set; }
    [Column("attempts")] public int Attempts { get; set; }
}
