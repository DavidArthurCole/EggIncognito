using System.Security.Claims;
using EggIdentity.Contract;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EggIncognito.Services;

public sealed class LoginSignIn {
    public async Task SignInAsync(HttpContext http, RedeemLoginCodeResponse result) {
        List<Claim> claims = [
            new(ClaimTypes.NameIdentifier, result.DiscordId ?? result.UserId.ToString()),
            new(ClaimTypes.Name, result.Username),
            new(AuthClaims.RoleClaim, result.Role),
            new(AuthClaims.UserIdClaim, result.UserId.ToString())
        ];
        if (!string.IsNullOrEmpty(result.Avatar))
            claims.Add(new Claim("urn:discord:avatar:hash", result.Avatar));

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = true });
    }
}
