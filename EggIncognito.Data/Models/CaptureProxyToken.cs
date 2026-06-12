using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// Per-user hosted-capture proxy credential. The plaintext token is shown once at mint; only the
// SHA-256 hex hash is stored. Username on the wire is the Discord id, so abuse maps to an identity.
[Table("capture_proxy_tokens")]
public class CaptureProxyToken
{
    [Key]
    [Column("discord_id")]
    public string DiscordId { get; set; } = "";

    [Column("token_hash")]
    public string TokenHash { get; set; } = "";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
