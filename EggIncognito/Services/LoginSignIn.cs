using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SyncKit.Contract;
using System.Security.Claims;

namespace EggIncognito.Services;

public sealed class LoginSignIn(ISupporterStatus supporters)
{
    public async Task SignInAsync(HttpContext http, RedeemLoginCodeResponse result)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.DiscordId ?? result.UserId.ToString()),
            new(ClaimTypes.Name, result.Username),
            new(UserRoles.ClaimType, result.Role),
            new(AuthClaims.UserIdClaim, result.UserId.ToString()),
        };
        if (!string.IsNullOrEmpty(result.Avatar))
            claims.Add(new Claim("urn:discord:avatar:hash", result.Avatar));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        var isSupporter = false;
        if (!string.IsNullOrEmpty(result.DiscordId))
            isSupporter = await supporters.CheckAsync(result.DiscordId, http.RequestAborted);
        SupporterClaims.Stamp(identity, isSupporter);

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}
