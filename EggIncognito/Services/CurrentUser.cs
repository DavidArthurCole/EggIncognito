using System.Security.Claims;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Http;

namespace EggIncognito.Services;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
    public Guid? UserId => IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(AuthClaims.UserIdClaim), out var id) ? id : null;
    public string? DiscordId => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.NameIdentifier) : null;
    public string? Username => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.Name) : null;
    public string? Avatar => IsAuthenticated ? Principal!.FindFirstValue("urn:discord:avatar:hash") : null;

    // Avatar claim is meant to hold a bare Discord hash, but the SyncKit identity widget flow has been
    // observed sending the full CDN URL instead - detect that so we don't wrap an already-complete URL.
    public string? AvatarUrl => Avatar switch
    {
        null or "" => null,
        var a when a.StartsWith("http://") || a.StartsWith("https://") => a,
        var a => $"https://cdn.discordapp.com/avatars/{DiscordId}/{a}.png",
    };

    public UserRole Role => UserRoles.Parse(IsAuthenticated ? Principal!.FindFirstValue(UserRoles.ClaimType) : null);
    public bool IsSupporter =>
        IsAuthenticated && Principal!.FindFirstValue(SupporterClaims.ClaimType) == "true";
    public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(Role, need);
}
