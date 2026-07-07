using AspNet.Security.OAuth.Discord;
using EggIncognito.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

// Login/logout + current-user JSON. Always present; AuthState.DiscordEnabled/AuthentikEnabled gate
// each provider's own challenge endpoint so one running without the other 404s instead of throwing
// against an unregistered scheme. Logout uses the combined AuthState.Enabled since it only clears
// the shared cookie scheme, not a provider-specific one. CurrentUser is always registered, anonymous
// when no auth middleware ran, so the controller constructs in every mode.
[ApiController]
public sealed class AuthController(AuthState authState, ICurrentUser currentUser) : ControllerBase
{
    private bool AuthOn => authState.Enabled;

    [HttpGet("/login")]
    public IActionResult Login([FromQuery] string returnUrl = "/")
    {
        if (!authState.DiscordEnabled) return NotFound();
        if (!Url.IsLocalUrl(returnUrl)) returnUrl = "/";
        return Challenge(
            new AuthenticationProperties { RedirectUri = returnUrl },
            DiscordAuthenticationDefaults.AuthenticationScheme);
    }

    [HttpGet("/auth")]
    public IActionResult AuthentikLogin([FromQuery] string returnUrl = "/")
    {
        if (!authState.AuthentikEnabled) return NotFound();
        if (!Url.IsLocalUrl(returnUrl)) returnUrl = "/";
        return Challenge(
            new AuthenticationProperties { RedirectUri = returnUrl },
            OpenIdConnectDefaults.AuthenticationScheme);
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
