using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("capture_proxy_addrs")]
public class CaptureProxyAddr {
    [Key]
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("discord_id")]
    public string? DiscordId { get; set; }

    [Column("addr")]
    public string Addr { get; set; } = "";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
