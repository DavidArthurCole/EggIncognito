using System.Security.Claims;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using SyncKit.Auth;
using SyncKit.Identity.Client;

namespace EggIncognito.Controllers;

[ApiController]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class AuthController(AuthState authState, ICurrentUser currentUser) : ControllerBase {
    [HttpPost("/logout")]
    public async Task<IActionResult> Logout() {
        if (!authState.Enabled) return NotFound();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var session = HttpContext.RequestServices.GetService<SessionCookieOptions>();
        if (session is not null) {
            string? sid = User.FindFirstValue(SessionClaims.SessionId);
            if (!string.IsNullOrEmpty(sid)) {
                var identity = HttpContext.RequestServices.GetService<IdentityApiClient>();
                if (identity is not null) {
                    try {
                        await identity.RevokeSessionAsync(sid, HttpContext.RequestAborted);
                    } catch (HttpRequestException) {
                    }
                }
            }

            SessionIssuer.ClearCookie(Response, session);
        }

        return Redirect("/");
    }

    [HttpGet("/api/auth/me")]
    public IActionResult Me() {
        if (!currentUser.IsAuthenticated)
            return Ok(new { authenticated = false });
        return Ok(new {
            authenticated = true,
            discordId = currentUser.DiscordId,
            username = currentUser.Username,
            avatar = currentUser.Avatar
        });
    }
}
