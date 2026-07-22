using System.Security.Claims;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Http;
using SyncKit.Auth;
using SyncKit.Contract;

namespace EggIncognito.Services;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private const string SessionSub = "sub";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    private string? Find(params string[] types) =>
        types.Select(t => Principal?.FindFirstValue(t)).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
    public Guid? UserId => IsAuthenticated && Guid.TryParse(Find(AuthClaims.UserIdClaim, SessionSub), out var id) ? id : null;
    public string? DiscordId => IsAuthenticated ? Find(ClaimTypes.NameIdentifier, SessionClaims.DiscordId) : null;
    public string? Username => IsAuthenticated ? Find(ClaimTypes.Name, SessionClaims.Name) : null;
    public string? Avatar => IsAuthenticated ? Find("urn:discord:avatar:hash", SessionClaims.Avatar) : null;

   
   
    public string? AvatarUrl => Avatar switch
    {
        null or "" => null,
        var a when a.StartsWith("http://") || a.StartsWith("https://") => a,
        var a => $"https://cdn.discordapp.com/avatars/{DiscordId}/{a}.png",
    };

    public UserRole Role => UserRoles.Parse(IsAuthenticated ? Find(AuthClaims.RoleClaim, SessionClaims.Role) : null);
    public bool IsSupporter =>
        IsAuthenticated && Principal!.FindFirstValue(SupporterClaims.ClaimType) == "true";
    public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(Role, need);
}
