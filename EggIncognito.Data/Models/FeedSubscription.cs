using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One feed subscriber: a Discord webhook (or generic HTTP target) wanting proto-update notifications for chosen platforms + trigger. The webhook URL is the capability; never echo it back.
[Table("feed_subscriptions")]
public sealed class FeedSubscription
{
    [Column("id")] public int Id { get; set; }
    [Column("kind")] public string Kind { get; set; } = "discord"; // discord | http
    [Column("target_url")] public string TargetUrl { get; set; } = "";
    [Column("platforms")] public string[] Platforms { get; set; } = ["android", "ios"];
    [Column("trigger")] public string Trigger { get; set; } = "proto_changed"; // proto_changed | new_version
    [Column("secret")] public string? Secret { get; set; } // HMAC key for http kind
    [Column("label")] public string? Label { get; set; }
    // Optional user-authored message with {{variable}} tokens (see FeedTemplate.Render); null/empty falls back to the built-in Discord embed.
    [Column("message_template")] public string? MessageTemplate { get; set; }
    [Column("owner_user_id")] public Guid? OwnerUserId { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("active")] public bool Active { get; set; } = true;
    [Column("last_delivery_at")] public DateTimeOffset? LastDeliveryAt { get; set; }
    [Column("fail_count")] public int FailCount { get; set; }
}
