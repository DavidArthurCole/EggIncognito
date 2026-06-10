using EggIncognito.Data.Models;

namespace EggIncognito.Services;

// The signed-in or anonymous user for the current request. Always registered; reports anonymous when
// no Discord auth middleware ran. ACL checks read Role/DiscordId from here.
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? DiscordId { get; }
    string? Username { get; }
    string? Avatar { get; }
    UserRole Role { get; }
    bool IsAtLeast(UserRole need);
}
