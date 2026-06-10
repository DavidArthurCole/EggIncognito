using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// A Discord-authenticated user. discord_id, the OAuth NameIdentifier, is the PK; role gates shared-DB
// writes.
[Table("users")]
public class User
{
    [Key]
    [Column("discord_id")]
    public string DiscordId { get; set; } = "";

    [Column("username")]
    public string Username { get; set; } = "";

    [Column("avatar")]
    public string? Avatar { get; set; }

    [Column("role")]
    public string Role { get; set; } = "viewer";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("last_login_at")]
    public DateTimeOffset LastLoginAt { get; set; }
}
