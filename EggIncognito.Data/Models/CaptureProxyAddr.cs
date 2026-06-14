using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One issued IPv6 proxy address per supporter. The address is derived deterministically from the
// Discord id (HMAC over a server secret) and is the capture credential: connecting to it proves the
// connector was issued it. Stored so the front door can reverse-map a destination address to a user.
[Table("capture_proxy_addrs")]
public class CaptureProxyAddr
{
    [Key]
    [Column("discord_id")]
    public string DiscordId { get; set; } = "";

    [Column("addr")]
    public string Addr { get; set; } = "";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
