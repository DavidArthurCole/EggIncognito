using AspNet.Security.OAuth.Discord;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EggIncognito.Controllers;

// Login/logout + current-user JSON. Always present; AuthState.DiscordEnabled/AuthentikEnabled gate
// each provider's own challenge endpoint so one running without the other 404s instead of throwing
// against an unregistered scheme. Logout uses the combined AuthState.Enabled since it only clears
// the shared cookie scheme, not a provider-specific one. CurrentUser is always registered, anonymous
// when no auth middleware ran, so the controller constructs in every mode.
[ApiController]
public sealed class AuthController(
    AuthState authState,
    ICurrentUser currentUser,
    IConfiguration config,
    IServiceProvider services,
    ILogger<AuthController> logger) : ControllerBase
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
        // A session started via Authentik carries an id_token (SaveTokens=true on that handler).
        // The OIDC handler reads it from the still-live cookie during its own SignOut handling, so
        // Cookies must be included in the SAME SignOut call, not signed out beforehand - it then
        // clears the cookie itself and redirects through Authentik's end_session_endpoint so the
        // IdP-side session ends too, not just the local one. A Discord-originated session has no
        // id_token, so this falls through to a plain cookie sign-out, same as before.
        var idToken = await HttpContext.GetTokenAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, "id_token");
        if (authState.AuthentikEnabled && !string.IsNullOrEmpty(idToken))
        {
            var props = new AuthenticationProperties { RedirectUri = "/" };
            return SignOut(props,
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme);
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    // Authentik back-channel logout notification: server-to-server POST with a signed logout_token,
    // no cookies/session context. Per spec (OIDC Back-Channel Logout 1.0 sec 2.6): verify signature +
    // iss/aud, require an "events" claim carrying the backchannel-logout member, forbid a "nonce"
    // claim, then revoke the sid so OnValidatePrincipal (AuthSetup.cs) kills that session on its next
    // request instead of riding out the 30-day cookie.
    [HttpPost("/auth/backchannel-logout")]
    [AllowAnonymous]
    public async Task<IActionResult> BackchannelLogout([FromForm] string? logout_token)
    {
        var oidcConfig = services.GetService<ConfigurationManager<OpenIdConnectConfiguration>>();
        var revocationStore = services.GetService<SessionRevocationStore>();
        if (!authState.AuthentikEnabled || oidcConfig is null || revocationStore is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(logout_token))
            return BadRequest();

        OpenIdConnectConfiguration discovery;
        try
        {
            discovery = await oidcConfig.GetConfigurationAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backchannel-logout: could not fetch Authentik discovery/JWKS");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var validationParams = new TokenValidationParameters
        {
            ValidIssuer = discovery.Issuer,
            IssuerSigningKeys = discovery.SigningKeys,
            ValidAudience = config["Authentik:ClientId"],
            ValidateLifetime = true,
        };

        ClaimsPrincipal principal;
        try
        {
            principal = new JwtSecurityTokenHandler().ValidateToken(logout_token, validationParams, out _);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backchannel-logout: logout_token failed validation");
            return BadRequest();
        }

        if (principal.FindFirstValue("nonce") is not null)
            return BadRequest(); // forbidden on a logout_token per spec; a normal id_token replayed here

        var events = principal.FindFirstValue("events");
        if (string.IsNullOrEmpty(events) || !events.Contains("backchannel-logout"))
            return BadRequest();

        var sid = principal.FindFirstValue("sid");
        if (string.IsNullOrEmpty(sid))
            return BadRequest();

        await revocationStore.RevokeAsync(sid, HttpContext.RequestAborted);
        return Ok();
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
