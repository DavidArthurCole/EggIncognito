using EggIncognito.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

[ApiController]
[EggIncognito.Services.Auth.ApiAccess(EggIncognito.Services.Auth.ApiAccessLevel.Public)]
public sealed class AuthController(AuthState authState, ICurrentUser currentUser) : ControllerBase {
    [HttpPost("/logout")]
    public async Task<IActionResult> Logout() {
        if (!authState.Enabled) return NotFound();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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
            avatar = currentUser.Avatar,
        });
    }
}
