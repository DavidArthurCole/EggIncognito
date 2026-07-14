using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SyncKit.Contract;
using SyncKit.Identity.Client;
using System.Security.Claims;

namespace EggIncognito.Controllers;

// Login/logout + current-user JSON. The only login path is the SyncKit widget: the browser gets a
// short-lived login code from the Identity host and posts it to /auth/redeem-code, which mints this
// app's cookie. No Discord/Authentik OAuth.
[ApiController]
public sealed class AuthController(
    AuthState authState,
    ICurrentUser currentUser,
    IServiceProvider services,
    ILogger<AuthController> logger) : ControllerBase
{
    // Exchanges a short-lived SyncKit login code for this app's own cookie.
    [HttpPost("/auth/redeem-code")]
    public async Task<IActionResult> RedeemCode([FromBody] RedeemCodeBody body)
    {
        var identity = services.GetService<IdentityApiClient>();
        if (!authState.WidgetEnabled || identity is null) return NotFound();
        if (string.IsNullOrWhiteSpace(body.Code)) return BadRequest();

        RedeemLoginCodeResponse result;
        try
        {
            result = await identity.RedeemAsync(body.Code, HttpContext.RequestAborted);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "redeem-code: code redemption failed");
            return BadRequest();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.DiscordId ?? result.UserId.ToString()),
            new(ClaimTypes.Name, result.Username),
            new(UserRoles.ClaimType, result.Role),
            new(AuthClaims.UserIdClaim, result.UserId.ToString()),
        };
        if (!string.IsNullOrEmpty(result.Avatar))
            claims.Add(new Claim("urn:discord:avatar:hash", result.Avatar));
        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(claimsIdentity);

        var isSupporter = false;
        if (!string.IsNullOrEmpty(result.DiscordId))
        {
            var checker = services.GetRequiredService<SupporterStatus>();
            isSupporter = await checker.CheckAsync(result.DiscordId, HttpContext.RequestAborted);
        }
        SupporterClaims.Stamp(claimsIdentity, isSupporter);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true });

        return Ok(new { discordId = result.DiscordId, username = result.Username, avatar = result.Avatar });
    }

    public sealed record RedeemCodeBody(string Code);

    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        if (!authState.Enabled) return NotFound();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    [HttpGet("/api/auth/me")]
    public IActionResult Me()
    {
        if (!currentUser.IsAuthenticated)
            return Ok(new { authenticated = false });
        return Ok(new
        {
            authenticated = true,
            discordId = currentUser.DiscordId,
            username = currentUser.Username,
            avatar = currentUser.Avatar,
        });
    }
}
