using EggIncognito.Data.Models;

namespace EggIncognito.Services;

// The signed-in or anonymous user for the current request. Always registered; reports anonymous when
// no auth middleware ran. ACL checks read Role/DiscordId/UserId from here.
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? DiscordId { get; }
    string? Username { get; }
    string? Avatar { get; }
    string? AvatarUrl { get; }
    UserRole Role { get; }
    bool IsSupporter { get; }
    bool IsAtLeast(UserRole need);
}
