using System.Security.Claims;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Http;
using SyncKit.Contract;

namespace EggIncognito.Services;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
    public Guid? UserId => IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(AuthClaims.UserIdClaim), out var id) ? id : null;
    public string? DiscordId => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.NameIdentifier) : null;
    public string? Username => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.Name) : null;
    public string? Avatar => IsAuthenticated ? Principal!.FindFirstValue("urn:discord:avatar:hash") : null;

   
   
    public string? AvatarUrl => Avatar switch
    {
        null or "" => null,
        var a when a.StartsWith("http://") || a.StartsWith("https://") => a,
        var a => $"https://cdn.discordapp.com/avatars/{DiscordId}/{a}.png",
    };

    public UserRole Role => UserRoles.Parse(IsAuthenticated ? Principal!.FindFirstValue(AuthClaims.RoleClaim) : null);
    public bool IsSupporter =>
        IsAuthenticated && Principal!.FindFirstValue(SupporterClaims.ClaimType) == "true";
    public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(Role, need);
}
