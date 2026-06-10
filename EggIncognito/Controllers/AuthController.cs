using AspNet.Security.OAuth.Discord;
using EggIncognito.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

// Login/logout + current-user JSON. The controller is always present; AuthState.Enabled tells it
// whether Discord auth was wired this run. When auth is off, /login + /logout 404 and /api/auth/me
// reports anonymous. CurrentUser is always registered (anonymous when no auth middleware ran), so the
// controller constructs in both modes.
[ApiController]
public sealed class AuthController(AuthState authState, ICurrentUser currentUser) : ControllerBase
{
    private bool AuthOn => authState.Enabled;

    [HttpGet("/login")]
    public IActionResult Login([FromQuery] string returnUrl = "/")
    {
        if (!AuthOn) return NotFound();
        return Challenge(
            new AuthenticationProperties { RedirectUri = returnUrl },
            DiscordAuthenticationDefaults.AuthenticationScheme);
    }

    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        if (!AuthOn) return NotFound();
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
