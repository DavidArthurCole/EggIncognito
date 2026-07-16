using EggIncognito.Data.Models;

namespace EggIncognito.Services;

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
