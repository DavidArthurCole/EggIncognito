using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// Per-user random IPv6 proxy address. Stable across sessions; rotatable to kill a leaked one. Front door reverse-maps it to a user.
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
